using SwiftRide.Contracts.Events;
using SwiftRide.Messaging;
using SwiftRide.Security;
using MassTransit;
using MatchingService.Api;
using MatchingService.Application.Consumers;
using MatchingService.Application.Services;
using MatchingService.Domain.Interfaces;
using MatchingService.Domain.Pricing;
using MatchingService.Infrastructure.Data;
using MatchingService.Infrastructure.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSwiftRideAuthentication(builder.Configuration);

var kafkaBootstrapServers = builder.Configuration.GetBootstrapServers();

// OpenTelemetry Observability
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options => options.RecordException = true)
            .AddHttpClientInstrumentation()
            .AddSource("MassTransit")
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("MassTransit")
            .AddMeter("SwiftRide.Metrics")
            .AddOtlpExporter();
    });

// Add services to the container.
builder.Services.AddOpenApi();

// Register Redis Distributed Cache
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConn;
    options.InstanceName = "SwiftRide_";
});

// Register MongoDB Context
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<MongoDbSeeder>();
builder.Services.AddSingleton<FareCalculator>();
builder.Services.AddTransient<IPricingStrategy, StandardPricingStrategy>();

// Register Decorator for IPricingRepository
builder.Services.AddScoped<PricingRepository>();
builder.Services.AddScoped<IPricingRepository>(sp =>
    new CachedPricingRepository(
        sp.GetRequiredService<PricingRepository>(),
        sp.GetRequiredService<IDistributedCache>(),
        sp.GetRequiredService<ILogger<CachedPricingRepository>>()));

builder.Services.AddScoped<IDriverRepository, DriverRepository>();
builder.Services.AddScoped<PricingAppService>();
builder.Services.AddScoped<DriverMatchingAppService>();

builder.Services.AddSwiftRideKafka(rider =>
{
    rider.AddConsumer<TripRequestedConsumer>();
    rider.AddConsumer<RetryMatchingConsumer>();
    rider.AddConsumer<DriverAvailabilityConsumer>();

    rider.AddKafkaProducer<DriverLocationUpdatedEvent>(KafkaTopics.DriverLocationUpdated);
    rider.AddKafkaProducer<MatchingFailedEvent>(KafkaTopics.MatchingFailed);
    rider.AddKafkaProducer<TripOfferProposedEvent>(KafkaTopics.TripOfferProposed);

    rider.UsingKafka((context, kafka) =>
    {
        kafka.Host(kafkaBootstrapServers);

        const string group = "swiftride-matching";

        kafka.AddKafkaConsumerEndpoint<TripRequestedEvent, TripRequestedConsumer>(
            context, KafkaTopics.TripRequested, group, RetryMatching);
        kafka.AddKafkaConsumerEndpoint<RetryMatchingCommand, RetryMatchingConsumer>(
            context, KafkaTopics.MatchingRetry, group, RetryMatching);
        kafka.AddKafkaConsumerEndpoint<MatchingAttemptClosedEvent, DriverAvailabilityConsumer>(
            context, KafkaTopics.MatchingAttemptClosed, group, RetryMatching);
        kafka.AddKafkaConsumerEndpoint<DriverRejectedOfferEvent, DriverAvailabilityConsumer>(
            context, KafkaTopics.DriverRejectedOffer, group, RetryMatching);
        kafka.AddKafkaConsumerEndpoint<TripCancelledEvent, DriverAvailabilityConsumer>(
            context, KafkaTopics.TripCancelled, group, RetryMatching);
        kafka.AddKafkaConsumerEndpoint<TripCompletedEvent, DriverAvailabilityConsumer>(
            context, KafkaTopics.TripCompleted, group, RetryMatching);
    });
});

static void RetryMatching(IRetryConfigurator retry)
    => retry.Interval(3, TimeSpan.FromSeconds(1));

var app = builder.Build();
await KafkaMessaging.EnsureTopicsAsync(kafkaBootstrapServers);

// Initialize MongoDB indexes
await DatabaseStartup.RunAsync(redisConn, "MatchingService", async ct =>
{
    await using var scope = app.Services.CreateAsyncScope();
    var mongoContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    await mongoContext.InitializeIndexesAsync(ct);
    await scope.ServiceProvider.GetRequiredService<MongoDbSeeder>().SeedAsync(ct);
}, app.Logger);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { Service = "MatchingService", Status = "Running" }));
app.MapMatchingEndpoints();

app.Run();


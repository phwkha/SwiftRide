using SwiftRide.Contracts.Events;
using SwiftRide.Messaging;
using SwiftRide.Security;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TripService.Api;
using TripService.Application.Consumers;
using TripService.Api.Hubs;
using TripService.Infrastructure.BackgroundServices;
using TripService.Infrastructure.Data;
using TripService.Application.Services;
using TripService.Domain.Interfaces;
using TripService.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSwiftRideAuthentication(builder.Configuration, signalR: true);

var connectionString = builder.Configuration.GetConnectionString("TripDbConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Missing configuration: ConnectionStrings:TripDbConnection");

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

// Add SignalR with Redis Backplane
var signalRBuilder = builder.Services.AddSignalR(options =>
{
    // Keep idle driver connections alive through the gateway's 10-second activity timeout.
    options.KeepAliveInterval = TimeSpan.FromSeconds(5);
});
if (!string.IsNullOrEmpty(redisConn))
{
    signalRBuilder.AddStackExchangeRedis(redisConn, options =>
    {
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("SwiftRideSignalR");
    });
}

// Register SQL Server TripDbContext
builder.Services.AddDbContext<TripDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register Trip Repository & Decorator
builder.Services.AddScoped<TripRepository>();
builder.Services.AddScoped<CachedTripRepository>();

builder.Services.AddScoped<ITripReader>(sp =>
    sp.GetRequiredService<CachedTripRepository>());

builder.Services.AddScoped<ITripWriter>(sp =>
    sp.GetRequiredService<CachedTripRepository>());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<ITripQuoteClient, TripQuoteClient>(client =>
{
    var address = builder.Configuration["Downstream:MatchingService"] ?? "http://localhost:5248";
    client.BaseAddress = new Uri(address.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<TripAppService>();
builder.Services.AddScoped<ITripAppService>(sp => sp.GetRequiredService<TripAppService>());

builder.Services.AddSwiftRideKafka(rider =>
{
    rider.AddConsumer<MatchingFailedConsumer>();
    rider.AddConsumer<TripOfferProposedConsumer>();
    rider.AddConsumer<PaymentSettledConsumer>();
    rider.AddConsumer<PaymentFailedConsumer>();
    rider.AddConsumer<TripOfferDispatchedConsumer>();
    rider.AddConsumer<TripOfferExpiredConsumer>();
    rider.AddConsumer<DriverLocationUpdatedConsumer>();
    rider.AddConsumer<PaymentSettledSignalRConsumer>();
    rider.AddConsumer<TripAcceptedSignalRConsumer>();
    rider.AddConsumer<TripCancelledSignalRConsumer>();
    rider.AddConsumer<TripStatusChangedSignalRConsumer>();

    rider.AddKafkaProducer<TripRequestedEvent>(KafkaTopics.TripRequested);
    rider.AddKafkaProducer<TripAcceptedEvent>(KafkaTopics.TripAccepted);
    rider.AddKafkaProducer<TripOfferDispatchedEvent>(KafkaTopics.TripOfferDispatched);
    rider.AddKafkaProducer<TripOfferExpiredEvent>(KafkaTopics.TripOfferExpired);
    rider.AddKafkaProducer<MatchingAttemptClosedEvent>(KafkaTopics.MatchingAttemptClosed);
    rider.AddKafkaProducer<DriverRejectedOfferEvent>(KafkaTopics.DriverRejectedOffer);
    rider.AddKafkaProducer<RetryMatchingCommand>(KafkaTopics.MatchingRetry);
    rider.AddKafkaProducer<TripDroppedOffEvent>(KafkaTopics.TripDroppedOff);
    rider.AddKafkaProducer<TripCancelledEvent>(KafkaTopics.TripCancelled);
    rider.AddKafkaProducer<TripCompletedEvent>(KafkaTopics.TripCompleted);
    rider.AddKafkaProducer<TripStatusChangedEvent>(KafkaTopics.TripStatusChanged);

    rider.UsingKafka((context, kafka) =>
    {
        kafka.Host(kafkaBootstrapServers);

        const string tripGroup = "swiftride-trip";
        const string signalRGroup = "swiftride-trip-signalr";

        kafka.AddKafkaConsumerEndpoint<MatchingFailedEvent, MatchingFailedConsumer>(
            context, KafkaTopics.MatchingFailed, tripGroup, RetryTripConcurrency);
        kafka.AddKafkaConsumerEndpoint<TripOfferProposedEvent, TripOfferProposedConsumer>(
            context, KafkaTopics.TripOfferProposed, tripGroup, RetryTripConcurrency);
        kafka.AddKafkaConsumerEndpoint<PaymentSettledEvent, PaymentSettledConsumer>(
            context, KafkaTopics.PaymentSettled, tripGroup, RetryTripConcurrency);
        kafka.AddKafkaConsumerEndpoint<PaymentFailedEvent, PaymentFailedConsumer>(
            context, KafkaTopics.PaymentFailed, tripGroup, RetryTripConcurrency);
        kafka.AddKafkaConsumerEndpoint<TripOfferDispatchedEvent, TripOfferDispatchedConsumer>(
            context, KafkaTopics.TripOfferDispatched, signalRGroup);
        kafka.AddKafkaConsumerEndpoint<TripOfferExpiredEvent, TripOfferExpiredConsumer>(
            context, KafkaTopics.TripOfferExpired, signalRGroup);
        kafka.AddKafkaConsumerEndpoint<DriverLocationUpdatedEvent, DriverLocationUpdatedConsumer>(
            context, KafkaTopics.DriverLocationUpdated, signalRGroup);
        kafka.AddKafkaConsumerEndpoint<PaymentSettledEvent, PaymentSettledSignalRConsumer>(
            context, KafkaTopics.PaymentSettled, signalRGroup);
        kafka.AddKafkaConsumerEndpoint<TripAcceptedEvent, TripAcceptedSignalRConsumer>(
            context, KafkaTopics.TripAccepted, signalRGroup);
        kafka.AddKafkaConsumerEndpoint<TripCancelledEvent, TripCancelledSignalRConsumer>(
            context, KafkaTopics.TripCancelled, signalRGroup);
        kafka.AddKafkaConsumerEndpoint<TripStatusChangedEvent, TripStatusChangedSignalRConsumer>(
            context, KafkaTopics.TripStatusChanged, signalRGroup);
    });
});

static void RetryTripConcurrency(IRetryConfigurator retry)
{
    retry.Handle<DbUpdateConcurrencyException>();
    retry.Immediate(3);
}

// Register Outbox publisher background service
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHostedService<MatchingDeadlineService>();

var app = builder.Build();
await KafkaMessaging.EnsureTopicsAsync(kafkaBootstrapServers);

// Khởi tạo database và thêm dữ liệu mẫu
await DatabaseStartup.RunAsync(redisConn, "TripService", async ct =>
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TripDbContext>();
    await TripDbSeeder.SeedAsync(dbContext, ct);
}, app.Logger);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Map SignalR hub
app.MapHub<TripHub>("/hubs/trips", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();

app.MapGet("/", () => Results.Ok(new { Service = "TripService", Status = "Running" }));
app.MapTripEndpoints();

app.Run();

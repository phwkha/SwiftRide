using SwiftRide.Contracts.Events;
using SwiftRide.Messaging;
using SwiftRide.Security;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaymentService.Api;
using PaymentService.Application.Consumers;
using PaymentService.Application.Services;
using PaymentService.Domain.Factories;
using PaymentService.Domain.Interfaces;
using PaymentService.Infrastructure.BackgroundServices;
using PaymentService.Infrastructure.Data;
using PaymentService.Infrastructure.Repositories;
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSwiftRideAuthentication(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("PaymentDbConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Missing configuration: ConnectionStrings:PaymentDbConnection");

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
builder.Services.AddExceptionHandler<PaymentExceptionHandler>();
builder.Services.AddProblemDetails();

// Register Redis Distributed Cache and Connection Multiplexer
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConn;
    options.InstanceName = "SwiftRide_";
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConn));

builder.Services.AddSingleton<PaymentService.Infrastructure.RedisIdempotencyGuard>();

// Register PostgreSQL PaymentDbContext
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(connectionString));

// Application services & Factory Pattern
builder.Services.AddSingleton<PaymentMethodFactory>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<PaymentAppService>();
builder.Services.AddScoped<PaymentRequestAuthorization>();
builder.Services.AddHttpClient<ITripSnapshotClient, TripSnapshotClient>(client =>
{
    var baseUrl = builder.Configuration["Downstream:TripService"] ?? "http://localhost:5105";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddSwiftRideKafka(rider =>
{
    rider.AddConsumer<TripDroppedOffConsumer>();

    rider.AddKafkaProducer<PaymentSettledEvent>(KafkaTopics.PaymentSettled);
    rider.AddKafkaProducer<PaymentFailedEvent>(KafkaTopics.PaymentFailed);
    rider.AddKafkaProducer<PaymentRefundedEvent>(KafkaTopics.PaymentRefunded);

    rider.UsingKafka((context, kafka) =>
    {
        kafka.Host(kafkaBootstrapServers);

        const string group = "swiftride-payment";

        kafka.AddKafkaConsumerEndpoint<TripDroppedOffEvent, TripDroppedOffConsumer>(
            context, KafkaTopics.TripDroppedOff, group);
    });
});

// Register Outbox publisher background service
builder.Services.AddHostedService<PaymentOutboxPublisherService>();

var app = builder.Build();
await KafkaMessaging.EnsureTopicsAsync(kafkaBootstrapServers);

// Tao schema PayDB va seed vi mau
await DatabaseStartup.RunAsync(redisConn, "PaymentService", async ct =>
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await PaymentDbSeeder.SeedAsync(dbContext, ct);
}, app.Logger);

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { Service = "PaymentService", Status = "Running" }));
app.MapPaymentEndpoints();

app.Run();

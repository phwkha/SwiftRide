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

foreach (var requiredConnection in new[] { "PaymentDbConnection", "RabbitMq" })
{
    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(requiredConnection)))
        throw new InvalidOperationException($"Missing configuration: ConnectionStrings:{requiredConnection}");
}

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
var connectionString = builder.Configuration.GetConnectionString("PaymentDbConnection")
    ?? throw new InvalidOperationException("Missing configuration: ConnectionStrings:PaymentDbConnection");

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(connectionString));

// Application services & Factory Pattern
builder.Services.AddSingleton<PaymentMethodFactory>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<PaymentAppService>();

// Register MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<TripDroppedOffConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqConnection = builder.Configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("Missing configuration: ConnectionStrings:RabbitMq");
        cfg.Host(new Uri(rabbitMqConnection));
        cfg.ConfigureEndpoints(context);
    });
});

// Register Outbox publisher background service
builder.Services.AddHostedService<PaymentOutboxPublisherService>();

var app = builder.Build();

// Tao schema PayDB va seed vi mau
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await PaymentDbSeeder.SeedAsync(dbContext);
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { Service = "PaymentService", Status = "Running" }));
app.MapPaymentEndpoints();

app.Run();

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

foreach (var requiredConnection in new[] { "RabbitMq" })
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

// Register MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<TripRequestedConsumer>();
    x.AddConsumer<RetryMatchingConsumer>();
    x.AddConsumer<DriverAvailabilityConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqConnection = builder.Configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("Missing configuration: ConnectionStrings:RabbitMq");
        cfg.Host(new Uri(rabbitMqConnection));
        cfg.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromSeconds(1)));
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

// Initialize MongoDB indexes
using (var scope = app.Services.CreateScope())
{
    var mongoContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    await mongoContext.InitializeIndexesAsync();
    await scope.ServiceProvider.GetRequiredService<MongoDbSeeder>().SeedAsync();
}

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


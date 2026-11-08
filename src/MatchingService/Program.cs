using MassTransit;
using MatchingService.Api;
using MatchingService.Application.Consumers;
using MatchingService.Application.DTOs;
using MatchingService.Application.Services;
using MatchingService.Domain.Interfaces;
using MatchingService.Domain.Pricing;
using MatchingService.Infrastructure.Data;
using MatchingService.Infrastructure.Repositories;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SwiftRide.Contracts.Events;

var builder = WebApplication.CreateBuilder(args);

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

// Register MongoDB Context
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<MongoDbSeeder>();
builder.Services.AddSingleton<FareCalculator>();
builder.Services.AddTransient<IPricingStrategy, StandardPricingStrategy>();
builder.Services.AddScoped<IPricingRepository, PricingRepository>();
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
            ?? "amqp://guest:guest@localhost:5672";
        cfg.Host(new Uri(rabbitMqConnection));
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

app.MapGet("/", () => Results.Ok(new { Service = "MatchingService", Status = "Running" }));
app.MapMatchingEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/simulate-trip-requested", async (TripRequestedEvent request, IPublishEndpoint publishEndpoint, HttpContext http, CancellationToken ct) =>
    {
        await publishEndpoint.Publish(request, ct);
        return Results.Accepted(value: ApiResponse<OperationResponse>.Success(
            new OperationResponse("Published"), "TripRequestedEvent published", http.TraceIdentifier));
    }).WithName("SimulateTripRequested");
}

app.Run();


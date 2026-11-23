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

foreach (var requiredConnection in new[] { "TripDbConnection", "RabbitMq" })
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
var connectionString = builder.Configuration.GetConnectionString("TripDbConnection")
    ?? throw new InvalidOperationException("Missing configuration: ConnectionStrings:TripDbConnection");

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
builder.Services.AddScoped<PaymentSettledConsumer>();
builder.Services.AddScoped<PaymentFailedConsumer>();

// Register MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MatchingFailedConsumer>();
    x.AddConsumer<TripOfferProposedConsumer>();
    x.AddConsumer<PaymentSettledConsumer>();
    x.AddConsumer<PaymentFailedConsumer>();
    x.AddConsumer<TripOfferDispatchedConsumer>();
    x.AddConsumer<TripOfferExpiredConsumer>();
    x.AddConsumer<DriverLocationUpdatedConsumer>();
    x.AddConsumer<PaymentSettledSignalRConsumer>();
    x.AddConsumer<TripAcceptedSignalRConsumer>();
    x.AddConsumer<TripCancelledSignalRConsumer>();
    x.AddConsumer<TripStatusChangedSignalRConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqConnection = builder.Configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("Missing configuration: ConnectionStrings:RabbitMq");
        cfg.Host(new Uri(rabbitMqConnection));
        cfg.UseMessageRetry(retry =>
        {
            retry.Handle<DbUpdateConcurrencyException>();
            retry.Immediate(3);
        });
        cfg.ConfigureEndpoints(context);
    });
});

// Register Outbox publisher background service
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHostedService<MatchingDeadlineService>();

var app = builder.Build();

// Khởi tạo database và thêm dữ liệu mẫu
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TripDbContext>();
    await TripDbSeeder.SeedAsync(dbContext);
}

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

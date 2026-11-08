using MassTransit;
using Microsoft.EntityFrameworkCore;
using TripService.Api;
using TripService.Application.Consumers;
using TripService.Api.Hubs;
using TripService.Infrastructure.BackgroundServices;
using TripService.Infrastructure.Data;
using TripService.Application.Services;
using TripService.Domain.Interfaces;
using TripService.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Add SignalR
builder.Services.AddSignalR();

// Register SQL Server TripDbContext
var connectionString = builder.Configuration.GetConnectionString("TripDbConnection")
    ?? "Server=localhost,1433;Database=TripDB;User Id=sa;Password=Password123!;TrustServerCertificate=True;MultipleActiveResultSets=true";

builder.Services.AddDbContext<TripDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register Trip Repository
builder.Services.AddScoped<TripRepository>();

builder.Services.AddScoped<ITripReader>(sp =>
    sp.GetRequiredService<TripRepository>());

builder.Services.AddScoped<ITripWriter>(sp =>
    sp.GetRequiredService<TripRepository>());

builder.Services.AddScoped<ITripAppService, TripAppService>();
builder.Services.AddScoped<MatchingFailedConsumer>();
builder.Services.AddScoped<PaymentSettledConsumer>();
builder.Services.AddScoped<PaymentFailedConsumer>();

// Register MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MatchingFailedConsumer>();
    x.AddConsumer<PaymentSettledConsumer>();
    x.AddConsumer<PaymentFailedConsumer>();
    x.AddConsumer<TripOfferDispatchedConsumer>();
    x.AddConsumer<TripOfferExpiredConsumer>();
    x.AddConsumer<DriverLocationUpdatedConsumer>();
    x.AddConsumer<PaymentSettledSignalRConsumer>();
    x.AddConsumer<TripAcceptedSignalRConsumer>();
    x.AddConsumer<TripCancelledSignalRConsumer>();
    x.AddConsumer<MatchingFailedSignalRConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqConnection = builder.Configuration.GetConnectionString("RabbitMq")
            ?? "amqp://guest:guest@localhost:5672";
        cfg.Host(new Uri(rabbitMqConnection));
        cfg.ConfigureEndpoints(context);
    });
});

// Register Outbox publisher background service
builder.Services.AddHostedService<OutboxPublisherService>();

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

// Map SignalR hub
app.MapHub<TripHub>("/hubs/trips");

app.MapGet("/", () => Results.Ok(new { Service = "TripService", Status = "Running" }));
app.MapTripEndpoints();

app.Run();

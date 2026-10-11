using MassTransit;
using Microsoft.EntityFrameworkCore;
using TripService.Infrastructure.BackgroundServices;
using TripService.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Register SQL Server TripDbContext
var connectionString = builder.Configuration.GetConnectionString("TripDbConnection")
    ?? "Server=localhost,1433;Database=TripDB;User Id=sa;Password=Password123!;TrustServerCertificate=True;MultipleActiveResultSets=true";

builder.Services.AddDbContext<TripDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { Service = "TripService", Status = "Running" }));

app.Run();

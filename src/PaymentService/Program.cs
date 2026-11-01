using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService.Api;
using PaymentService.Application.Consumers;
using PaymentService.Application.Services;
using PaymentService.Domain.Factories;
using PaymentService.Infrastructure.BackgroundServices;
using PaymentService.Infrastructure.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<PaymentExceptionHandler>();
builder.Services.AddProblemDetails();

// Register PostgreSQL PaymentDbContext
var connectionString = builder.Configuration.GetConnectionString("PaymentDbConnection")
    ?? "Host=localhost;Port=5432;Database=PayDB;Username=postgres;Password=postgrespassword";

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(connectionString));

// Application services & Factory Pattern
builder.Services.AddSingleton<PaymentMethodFactory>();
builder.Services.AddScoped<PaymentAppService>();

// Register MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<TripDroppedOffConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqConnection = builder.Configuration.GetConnectionString("RabbitMq")
            ?? "amqp://guest:guest@localhost:5672";
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

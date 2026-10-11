using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Register PostgreSQL PaymentDbContext
var connectionString = builder.Configuration.GetConnectionString("PaymentDbConnection")
    ?? "Host=localhost;Port=5432;Database=PayDB;Username=postgres;Password=postgrespassword";

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(connectionString));

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { Service = "PaymentService", Status = "Running" }));

app.Run();

using Microsoft.EntityFrameworkCore;
using TripService.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Register SQL Server TripDbContext
var connectionString = builder.Configuration.GetConnectionString("TripDbConnection")
    ?? "Server=localhost,1433;Database=TripDB;User Id=sa;Password=Password123!;TrustServerCertificate=True;MultipleActiveResultSets=true";

builder.Services.AddDbContext<TripDbContext>(options =>
    options.UseSqlServer(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { Service = "TripService", Status = "Running" }));

app.Run();

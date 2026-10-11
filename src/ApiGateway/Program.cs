var builder = WebApplication.CreateBuilder(args);

// Add YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Map reverse proxy routes
app.MapReverseProxy();

// Health check endpoint
app.MapGet("/", () => Results.Ok(new { Service = "ApiGateway", Status = "Running" }));

app.Run();

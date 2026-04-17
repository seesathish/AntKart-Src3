using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Logging;
using AK.ShoppingCart.API.Endpoints;
using AK.ShoppingCart.API.Middleware;
using AK.ShoppingCart.Application.Extensions;
using AK.ShoppingCart.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilogLogging();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AK.ShoppingCart API", Version = "v1", Description = "AntKart Shopping Cart Microservice — Redis-backed cart management" });
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlerMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "AK.ShoppingCart API v1"));

app.MapCartEndpoints();
app.MapDefaultHealthChecks();

app.Run();

public partial class Program { }

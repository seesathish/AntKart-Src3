using AK.BuildingBlocks.Authentication;
using AK.BuildingBlocks.Configuration;
using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Logging;
using AK.BuildingBlocks.Swagger;
using AK.Products.API.Endpoints;
using AK.Products.API.Extensions;
using AK.BuildingBlocks.Middleware;
using AK.Products.Application.Extensions;
using AK.Products.Infrastructure.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// M3 Step 1 — load configuration/secrets from Azure Key Vault (when KeyVault:Uri is set),
// using this service's own Entra identity, before anything reads configuration.
builder.Configuration.AddAzureKeyVaultConfiguration(builder.Configuration);

builder.AddSerilogLogging();

// Non-secret startup confirmation: record WHETHER Key Vault configuration was loaded, and
// from which vault URI (a non-secret value). Secret values are never logged.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (string.IsNullOrWhiteSpace(keyVaultUri))
    Log.Information("Key Vault configuration source not configured (KeyVault:Uri absent); using local configuration only");
else
    Log.Information("Key Vault configuration loaded from {KeyVaultUri}", keyVaultUri);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AK.Products API", Version = "v1", Description = "AntKart Products Microservice — Men, Women & Kids Dress Collections" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlerMiddleware>();

app.UseSwaggerInDevelopment("AK.Products API v1");

app.UseKeycloakAuth();

var seedEnabled = app.Environment.IsDevelopment() ||
    string.Equals(app.Configuration["SEED_DATABASE"], "true", StringComparison.OrdinalIgnoreCase);
if (seedEnabled)
    await app.SeedDatabaseAsync();

app.MapProductEndpoints();
app.MapDefaultHealthChecks();

app.Run();

public partial class Program { }

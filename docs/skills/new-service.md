# Skill: Add a New Microservice

**Purpose:** Scaffold a complete new `AK.<Name>` microservice — folder structure, all four layers, `Program.cs`, Dockerfile, Docker Compose entry, design doc, and all registration steps. Follow every step in order; skipping any step causes build or runtime failures.

---

## When to Use
When the team decides a new bounded context needs its own deployable service — e.g. `AK.Reviews`, `AK.Inventory`, `AK.Analytics`.

## Prerequisites
- Solution builds clean: `dotnet build` → 0 errors
- Docker stack is down or you are prepared to rebuild it

---

## Step 1 — Create the Folder Structure

```
AK.<Name>/
  AK.<Name>.Domain/
  AK.<Name>.Application/
  AK.<Name>.Infrastructure/
  AK.<Name>.API/          (REST) or AK.<Name>.Grpc/ (gRPC)
  AK.<Name>.Tests/
```

No double-nesting. `AK.<Name>.Domain/` lives directly inside `AK.<Name>/`, not inside a second identically-named folder.

---

## Step 2 — Create .csproj Files

Use `net9.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` in every project.

**Domain** (no external dependencies):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\AK.BuildingBlocks\AK.BuildingBlocks\AK.BuildingBlocks.csproj" />
  </ItemGroup>
</Project>
```

**Application** (→ Domain, BuildingBlocks):
```xml
<ItemGroup>
  <ProjectReference Include="..\AK.<Name>.Domain\AK.<Name>.Domain.csproj" />
  <ProjectReference Include="..\..\AK.BuildingBlocks\AK.BuildingBlocks\AK.BuildingBlocks.csproj" />
  <PackageReference Include="MediatR" Version="12.4.1" />
  <PackageReference Include="FluentValidation" Version="11.*" />
  <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.*" />
</ItemGroup>
```

**Infrastructure** (→ Application):
```xml
<ItemGroup>
  <ProjectReference Include="..\AK.<Name>.Application\AK.<Name>.Application.csproj" />
  <!-- Add EF Core / MongoDB / Redis packages as needed -->
</ItemGroup>
```

**API** (→ Application, Infrastructure, BuildingBlocks):
```xml
<ItemGroup>
  <ProjectReference Include="..\AK.<Name>.Application\AK.<Name>.Application.csproj" />
  <ProjectReference Include="..\AK.<Name>.Infrastructure\AK.<Name>.Infrastructure.csproj" />
  <ProjectReference Include="..\..\AK.BuildingBlocks\AK.BuildingBlocks\AK.BuildingBlocks.csproj" />
  <PackageReference Include="Swashbuckle.AspNetCore" Version="7.*" />
</ItemGroup>
```

**Tests** (→ Application, Infrastructure, Domain — never API):
```xml
<ItemGroup>
  <ProjectReference Include="..\AK.<Name>.Application\AK.<Name>.Application.csproj" />
  <ProjectReference Include="..\AK.<Name>.Infrastructure\AK.<Name>.Infrastructure.csproj" />
  <ProjectReference Include="..\AK.<Name>.Domain\AK.<Name>.Domain.csproj" />
  <PackageReference Include="xunit" Version="2.9.*" />
  <PackageReference Include="Moq" Version="4.20.*" />
  <PackageReference Include="FluentAssertions" Version="7.*" />
  <Using Include="Xunit" />
</ItemGroup>
```

---

## Step 3 — Register in Solution

```bash
dotnet sln add AK.<Name>/AK.<Name>.Domain/AK.<Name>.Domain.csproj
dotnet sln add AK.<Name>/AK.<Name>.Application/AK.<Name>.Application.csproj
dotnet sln add AK.<Name>/AK.<Name>.Infrastructure/AK.<Name>.Infrastructure.csproj
dotnet sln add AK.<Name>/AK.<Name>.API/AK.<Name>.API.csproj
dotnet sln add AK.<Name>/AK.<Name>.Tests/AK.<Name>.Tests.csproj
```

---

## Step 4 — Implement Layers in Order

**Domain first:** Aggregate root entity (inherits `AK.BuildingBlocks.DDD.Entity` for Guid ID, or `StringEntity` for MongoDB string IDs), value objects (inherit `ValueObject`), domain events (implement `IDomainEvent`). Private setters + static `Create(...)` factory method. No EF or infrastructure attributes.

**Application second:** Commands, queries, handlers, validators, DTOs, mappers. Wire MediatR and ValidationBehavior in `ServiceCollectionExtensions.AddApplication()`:
```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly));
    services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    return services;
}
```

**Infrastructure third:** DbContext / repository implementations, EF Core fluent configuration in `OnModelCreating`, seed data, MassTransit consumers.

**API last:** `Program.cs`, endpoint classes, Dockerfile.

---

## Step 5 — Wire Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.AddSerilogLogging();
builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSwaggerInDevelopment("<Name> API");
app.UseKeycloakAuth();
app.MapDefaultHealthChecks();
app.Map<Name>Endpoints();

app.Run();
```

Shared `ExceptionHandlerMiddleware` from BuildingBlocks handles: `ValidationException`→400, `UnauthorizedAccessException`→403, `KeyNotFoundException`→404, `InvalidOperationException`→409, `Exception`→500.

---

## Step 6 — Add Dockerfile

Place `Dockerfile` inside `AK.<Name>/AK.<Name>.API/`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AK.<Name>/AK.<Name>.API/AK.<Name>.API.csproj", "AK.<Name>/AK.<Name>.API/"]
COPY ["AK.<Name>/AK.<Name>.Application/AK.<Name>.Application.csproj", "AK.<Name>/AK.<Name>.Application/"]
COPY ["AK.<Name>/AK.<Name>.Infrastructure/AK.<Name>.Infrastructure.csproj", "AK.<Name>/AK.<Name>.Infrastructure/"]
COPY ["AK.<Name>/AK.<Name>.Domain/AK.<Name>.Domain.csproj", "AK.<Name>/AK.<Name>.Domain/"]
COPY ["AK.BuildingBlocks/AK.BuildingBlocks/AK.BuildingBlocks.csproj", "AK.BuildingBlocks/AK.BuildingBlocks/"]
COPY ["nuget.config", "."]
RUN dotnet restore "AK.<Name>/AK.<Name>.API/AK.<Name>.API.csproj"
COPY . .
WORKDIR "/src/AK.<Name>/AK.<Name>.API"
RUN dotnet build "AK.<Name>.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AK.<Name>.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AK.<Name>.API.dll"]
```

---

## Step 7 — Add to docker-compose.yml

```yaml
  ak-<name>-api:
    image: antkart-<name>-api
    build:
      context: .
      dockerfile: AK.<Name>/AK.<Name>.API/Dockerfile
    container_name: antkart-<name>-api
    ports:
      - "808X:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Keycloak__Authority=http://keycloak:8080/realms/antkart
      - Keycloak__Audience=antkart-client
      - Keycloak__AdminUrl=http://keycloak:8080
    depends_on:
      keycloak:
        condition: service_healthy
      # add postgres/redis/etc as appropriate
```

Add the corresponding route in `ocelot.json` — see [add-gateway-route.md](add-gateway-route.md).

---

## Step 8 — Add launchSettings.json

Place in `AK.<Name>/AK.<Name>.API/Properties/launchSettings.json`. Pick the next available dev port (check existing services: 5077, 5079, 5080, 5085, 5086, 5087):

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:50XX",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Commit this file — VS generates it locally if missing, causing noise in `git status`.

---

## Step 9 — Build & Test Gates

```bash
dotnet build              # must be 0 errors
dotnet test               # all existing tests must still pass
```

Write at least these test categories before proceeding:
- Domain: entity creation, state transitions, invariant enforcement
- Application: command/query handler (mocked repository), validator (valid + invalid cases)
- Infrastructure: repository (EF InMemory or Moq)

---

## Step 10 — Create Design Doc

Create `AK.<Name>/<NAME>_TECHNICAL_DESIGN.md`. Sections to include:
- Overview & responsibilities
- Transport, database, port
- Architecture (DDD layers)
- Pattern stack (CQRS, Repository, UoW, etc.)
- Domain model (entity diagram)
- API endpoints table
- Integration events (published / consumed)
- Security (auth, IDOR pattern)
- Related ADRs
- Testing strategy

---

## Step 11 — Update CLAUDE.md

Add a `### ✅ AK.<Name>` section under **Completed Services** with: transport, database, patterns, operations, test count, Swagger URL, design doc link.

---

## Step 12 — Update README.md

- Add row to **Solution Structure** tree
- Add row to **Microservices** table (transport, database, Docker port, design doc link)
- Add row to **Tests** table with test count
- Add row to **Authorization** table

---

## Step 13 — Update Postman Collection

In `AntKart.postman_collection.json`:
- Add a new top-level folder named `AK.<Name>`
- Add one request per endpoint: method, URL (using `{{<name>Url}}` collection variable), sample body, auth Bearer token
- Add the collection variable `<name>Url` pointing to `http://localhost:50XX`

---

## Step 14 — Commit

```bash
git add .
git commit -m "Add AK.<Name> microservice"
git push origin master
```

---

## Common Mistakes

| Mistake | Consequence | Prevention |
|---------|-------------|------------|
| Double-nesting folders | Build fails — project references resolve wrong paths | Check: `AK.<Name>/AK.<Name>.Domain/` not `AK.<Name>/AK.<Name>.Domain/AK.<Name>.Domain/` |
| Referencing API layer from Tests | Circular dependency or unnecessary coupling | Tests reference Application + Infrastructure + Domain only |
| Missing `AddMemoryCache()` before `AddOcelot()` | Rate limiting silently disabled | Only applies to Gateway — not needed in service |
| No `USER $APP_UID` in Dockerfile | Container runs as root | Always add before `ENTRYPOINT` |
| Forgetting `nuget.config` COPY in Dockerfile | Restore pulls from Azure DevOps feed → 401 | Always `COPY ["nuget.config", "."]` |
| `AddServerHeader` not set | `Server: Kestrel` exposed | `builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false)` |
| userId in endpoint route or DTO | IDOR vulnerability | Always use `http.GetUserId()` — see [verify-idor.md](verify-idor.md) |

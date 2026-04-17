# AntKart

AntKart is a cloud-native e-commerce platform built as a collection of independently deployable microservices using .NET 9, Clean Architecture, and DDD principles.

## Microservices

| Service | Description | Transport | Database | Technical Design |
|---------|-------------|-----------|----------|-----------------|
| [AK.Products](src/AK.Products.API/AK.Products.API) | Product catalogue — Men, Women & Kids dress collections | REST / Minimal API | MongoDB | [Design Doc](TECHNICAL_DESIGN.md) |
| [AK.Discount](src/AK.Discount.Grpc/AK.Discount.Grpc) | Discount coupon management for products | gRPC | SQLite | [Design Doc](DISCOUNT_TECHNICAL_DESIGN.md) |

## Shared Libraries

| Library | Purpose |
|---------|---------|
| [AK.BuildingBlocks](src/AK.BuildingBlocks/AK.BuildingBlocks) | Cross-cutting concerns — Serilog logging, health checks, `PagedResult<T>`, `Result<T>`, base exceptions, correlation ID middleware |

## Running Locally

### Prerequisites
- .NET 9 SDK
- Docker Desktop (for MongoDB and containerised runs)

### Start all services with Docker Compose
```bash
docker-compose up --build
```

| Service | URL |
|---------|-----|
| AK.Products REST API | http://localhost:8080 |
| AK.Products Swagger UI | http://localhost:8080/swagger |
| AK.Discount gRPC | http://localhost:8081 |
| Health — Products | http://localhost:8080/health |
| Health — Discount | http://localhost:8081/health |

### Start individual services (dev)
```bash
# Terminal 1 — Products API (port 5077)
cd src/AK.Products.API/AK.Products.API
dotnet run

# Terminal 2 — Discount gRPC (port 5001)
cd src/AK.Discount.Grpc/AK.Discount.Grpc
dotnet run
```

## Running Tests
```bash
dotnet test
```
- AK.Products.Tests: 45 tests
- AK.Discount.Tests: 11 tests
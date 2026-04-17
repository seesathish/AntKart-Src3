# AntKart

AntKart is a cloud-native e-commerce platform built as a collection of independently deployable microservices using .NET 9, Clean Architecture, and DDD principles.

## Solution Structure

```
AntKart/
├── AK.Products/                          # Product catalogue microservice
│   ├── AK.Products.Domain/
│   ├── AK.Products.Application/
│   ├── AK.Products.Infrastructure/
│   ├── AK.Products.API/
│   ├── AK.Products.Tests/
│   └── PRODUCTS_TECHNICAL_DESIGN.md
├── AK.Discount/                          # Discount coupon microservice
│   ├── AK.Discount.Domain/
│   ├── AK.Discount.Application/
│   ├── AK.Discount.Infrastructure/
│   ├── AK.Discount.Grpc/
│   ├── AK.Discount.Tests/
│   └── DISCOUNT_TECHNICAL_DESIGN.md
├── AK.ShoppingCart/                      # Shopping cart microservice
│   ├── AK.ShoppingCart.Domain/
│   ├── AK.ShoppingCart.Application/
│   ├── AK.ShoppingCart.Infrastructure/
│   ├── AK.ShoppingCart.API/
│   ├── AK.ShoppingCart.Tests/
│   └── SHOPPING_CART_TECHNICAL_DESIGN.md
├── AK.BuildingBlocks/                    # Shared cross-cutting library
│   └── AK.BuildingBlocks/
├── AntKart.postman_collection.json       # Unified API collection (all services)
├── docker-compose.yml
├── docker-compose.override.yml
└── nuget.config
```

## Microservices

| Service | Transport | Database | Design Doc |
|---------|-----------|----------|------------|
| [AK.Products](AK.Products/AK.Products.API) | REST — Minimal API | MongoDB | [Products Design](AK.Products/PRODUCTS_TECHNICAL_DESIGN.md) |
| [AK.Discount](AK.Discount/AK.Discount.Grpc) | gRPC | SQLite | [Discount Design](AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md) |
| [AK.ShoppingCart](AK.ShoppingCart/AK.ShoppingCart.API) | REST — Minimal API | Redis | [ShoppingCart Design](AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md) |

## Shared Libraries

| Library | Purpose |
|---------|---------|
| [AK.BuildingBlocks](AK.BuildingBlocks/AK.BuildingBlocks) | Serilog logging, health checks, `PagedResult<T>`, `Result<T>`, base exceptions, correlation ID middleware |

## API Testing

Import **[AntKart.postman_collection.json](AntKart.postman_collection.json)** into Postman.

| Collection Variable | Default Value | Description |
|--------------------|---------------|-------------|
| `productsUrl` | `http://localhost:5077` | Products REST API base URL |
| `discountGrpc` | `localhost:5001` | Discount gRPC host |
| `cartUrl` | `http://localhost:5079` | ShoppingCart REST API base URL |
| `productId` | `replace-with-actual-id` | MongoDB ObjectId of a product |

> **AK.Discount** is a gRPC service. The collection includes grpcurl commands as descriptions. For a native gRPC UI, use Postman's **New > gRPC Request** with the proto file at `AK.Discount/AK.Discount.Grpc/Protos/discount.proto`.

## Running Locally

### Prerequisites
- .NET 9 SDK
- Docker Desktop

### All services via Docker Compose
```bash
docker-compose up --build
```

| Service | URL |
|---------|-----|
| AK.Products REST API | http://localhost:8080 |
| AK.Products Swagger UI | http://localhost:8080/swagger |
| AK.Discount gRPC | http://localhost:8081 |
| AK.ShoppingCart REST API | http://localhost:8082 |
| AK.ShoppingCart Swagger UI | http://localhost:8082/swagger |
| Products Health | http://localhost:8080/health |
| Discount Health | http://localhost:8081/health |
| ShoppingCart Health | http://localhost:8082/health |

### Individual services (dev)
```bash
# Terminal 1 — Products API  →  http://localhost:5077/swagger
cd AK.Products/AK.Products.API && dotnet run

# Terminal 2 — Discount gRPC  →  localhost:5001
cd AK.Discount/AK.Discount.Grpc && dotnet run

# Terminal 3 — ShoppingCart API  →  http://localhost:5079/swagger  (requires Redis)
cd AK.ShoppingCart/AK.ShoppingCart.API && dotnet run
```

## Tests
```bash
dotnet test
```
| Project | Tests |
|---------|-------|
| AK.Products.Tests | 165 |
| AK.Discount.Tests | 51 |
| AK.ShoppingCart.Tests | 88 |
| **Total** | **304** |

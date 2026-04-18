using AK.Products.Application.Commands.BulkInsertProducts;
using AK.Products.Application.Commands.BulkUpdateProducts;
using AK.Products.Application.Commands.CreateProduct;
using AK.Products.Application.Commands.DeleteProduct;
using AK.Products.Application.Commands.UpdateProduct;
using AK.Products.Application.DTOs;
using AK.Products.Application.Queries.GetProductById;
using AK.Products.Application.Queries.GetProducts;
using AK.Products.Application.Queries.GetProductsByCategory;
using AK.Products.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AK.Products.API.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products");

        // GET /api/v1/products
        group.MapGet("/", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] Gender? gender,
            [FromQuery] string? category,
            [FromQuery] string? search,
            [FromQuery] bool? featured,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var query = new GetProductsQuery(
                Page: page > 0 ? page : 1,
                PageSize: pageSize > 0 ? pageSize : 20,
                Gender: gender,
                Category: category,
                SearchTerm: search,
                IsFeatured: featured);
            var result = await mediator.Send(query, ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetProducts")
        .WithSummary("Get all products with optional filtering and paging");

        // GET /api/v1/products/{id}
        group.MapGet("/{id}", async (string id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductByIdQuery(id), ct);
            return result is null ? Results.NotFound(new { error = $"Product '{id}' not found" }) : Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetProductById")
        .WithSummary("Get a product by ID");

        // GET /api/v1/products/category/{category}
        group.MapGet("/category/{category}", async (string category, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductsByCategoryQuery(category), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetProductsByCategory")
        .WithSummary("Get all products by category name");

        // POST /api/v1/products
        group.MapPost("/", async (CreateProductDto dto, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateProductCommand(dto), ct);
            return Results.Created($"/api/v1/products/{result.Id}", result);
        })
        .RequireAuthorization("admin")
        .WithName("CreateProduct")
        .WithSummary("Create a new product (Admin only)");

        // PUT /api/v1/products/{id}
        group.MapPut("/{id}", async (string id, UpdateProductDto dto, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdateProductCommand(id, dto), ct);
            return Results.Ok(result);
        })
        .RequireAuthorization("admin")
        .WithName("UpdateProduct")
        .WithSummary("Update an existing product (Admin only)");

        // DELETE /api/v1/products/{id}
        group.MapDelete("/{id}", async (string id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new DeleteProductCommand(id), ct);
            return Results.NoContent();
        })
        .RequireAuthorization("admin")
        .WithName("DeleteProduct")
        .WithSummary("Delete a product (Admin only)");

        // POST /api/v1/products/bulk-insert
        group.MapPost("/bulk-insert", async (List<CreateProductDto> products, IMediator mediator, CancellationToken ct) =>
        {
            var count = await mediator.Send(new BulkInsertProductsCommand(products), ct);
            return Results.Ok(new { inserted = count, message = $"Successfully inserted {count} products" });
        })
        .RequireAuthorization("admin")
        .WithName("BulkInsertProducts")
        .WithSummary("Bulk insert multiple products (Admin only)");

        // PUT /api/v1/products/bulk-update
        group.MapPut("/bulk-update", async (List<BulkUpdateProductDto> updates, IMediator mediator, CancellationToken ct) =>
        {
            var count = await mediator.Send(new BulkUpdateProductsCommand(updates), ct);
            return Results.Ok(new { updated = count, message = $"Successfully updated {count} products" });
        })
        .RequireAuthorization("admin")
        .WithName("BulkUpdateProducts")
        .WithSummary("Bulk update multiple products (Admin only)");

        // GET /api/v1/products/men
        group.MapGet("/men", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductsQuery(Gender: Gender.Men), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetMenProducts")
        .WithSummary("Get all men's products");

        // GET /api/v1/products/women
        group.MapGet("/women", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductsQuery(Gender: Gender.Women), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetWomenProducts")
        .WithSummary("Get all women's products");

        // GET /api/v1/products/kids
        group.MapGet("/kids", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductsQuery(Gender: Gender.Kids), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetKidsProducts")
        .WithSummary("Get all kids' products");

        // GET /api/v1/products/featured
        group.MapGet("/featured", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductsQuery(IsFeatured: true, PageSize: 50), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetFeaturedProducts")
        .WithSummary("Get all featured products");
    }
}

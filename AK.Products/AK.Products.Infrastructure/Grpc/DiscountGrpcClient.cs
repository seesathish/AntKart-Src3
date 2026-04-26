using AK.Discount.Grpc;
using AK.Products.Application.Interfaces;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AK.Products.Infrastructure.Grpc;

public sealed class DiscountGrpcSettings
{
    public string Address { get; init; } = string.Empty;
}

// gRPC client for AK.Discount — used by product query handlers to enrich product results
// with live discount prices without making a separate REST call.
//
// Uses IHttpClientFactory so the "discount-grpc" named client (registered in Infrastructure extensions)
// inherits the Polly resilience pipeline (retry + circuit breaker).
internal sealed class DiscountGrpcClient : IDiscountGrpcClient
{
    private readonly DiscountProtoService.DiscountProtoServiceClient _client;
    private readonly ILogger<DiscountGrpcClient> _logger;

    public DiscountGrpcClient(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscountGrpcSettings> settings,
        ILogger<DiscountGrpcClient> logger)
    {
        _logger = logger;
        // Reuse the named HttpClient so connection pooling and Polly policies apply.
        var httpClient = httpClientFactory.CreateClient("discount-grpc");
        var channel = GrpcChannel.ForAddress(settings.Value.Address,
            new GrpcChannelOptions { HttpClient = httpClient });
        _client = new DiscountProtoService.DiscountProtoServiceClient(channel);
    }

    public async Task<DiscountResult?> GetDiscountAsync(string productId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetDiscountAsync(
                new GetDiscountRequest { ProductId = productId },
                cancellationToken: ct);
            return new DiscountResult(response.Amount, response.DiscountType, response.IsActive);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // No coupon configured for this product — callers treat null as "no discount".
            return null;
        }
        catch (Exception ex)
        {
            // Discount service unavailable (network error, circuit open, etc.).
            // Return null so the product is still returned — just without a discounted price.
            // Product queries must never fail because the discount service is down.
            _logger.LogWarning(ex, "Failed to fetch discount for product {ProductId}", productId);
            return null;
        }
    }
}

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
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch discount for product {ProductId}", productId);
            return null;
        }
    }
}

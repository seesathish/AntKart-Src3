using AK.Products.Application.Common;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using MediatR;

namespace AK.Products.Application.Queries.GetProductById;

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly IUnitOfWork _uow;
    private readonly IDiscountGrpcClient _discountClient;

    public GetProductByIdQueryHandler(IUnitOfWork uow, IDiscountGrpcClient discountClient)
    {
        _uow = uow;
        _discountClient = discountClient;
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        var product = await _uow.Products.GetByIdAsync(request.Id, ct);
        if (product is null) return null;

        var discount = await _discountClient.GetDiscountAsync(product.Id, ct);

        decimal? discountedPrice = null;
        if (discount is { IsActive: true })
            discountedPrice = ProductMapper.ComputeDiscountedPrice(product.Price, discount.Amount, discount.DiscountType);

        return ProductMapper.ToDto(product, discountedPrice);
    }
}

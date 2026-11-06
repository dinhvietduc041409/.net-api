using DotnetApi.Models;

namespace DotnetApi.Contracts;

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    ProductStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateProductRequest(string? Name, string? Description, decimal? Price);

public sealed class UpdateProductRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public ProductStatus? Status { get; set; }
}

public static class ProductMappings
{
    public static ProductResponse ToResponse(this Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Price,
        product.Status,
        product.CreatedAt,
        product.UpdatedAt);
}

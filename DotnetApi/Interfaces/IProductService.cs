using DotnetApi.Contracts;

namespace DotnetApi.Interfaces;

public interface IProductService
{
    Task<IReadOnlyList<ProductResponse>> ListAsync(CancellationToken ct);

    Task<ProductResponse> GetAsync(Guid id, CancellationToken ct);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct);

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct);

    Task<ProductResponse> ArchiveAsync(Guid id, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}

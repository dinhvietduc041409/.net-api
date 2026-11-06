using DotnetApi.Contracts;
using DotnetApi.Data;
using DotnetApi.Infrastructure;
using DotnetApi.Interfaces;
using DotnetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DotnetApi.Services;

public sealed class ProductService(AppDbContext db) : IProductService
{
    public async Task<IReadOnlyList<ProductResponse>> ListAsync(CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking()
            .OrderByDescending(product => product.CreatedAt)
            .ToListAsync(ct);

        return products.Select(product => product.ToResponse()).ToList();
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .SingleOrDefaultAsync(product => product.Id == id, ct);

        return product is null
            ? throw new NotFoundException("Product", id)
            : product.ToResponse();
    }

    public async Task<ProductResponse> CreateAsync(
        CreateProductRequest request,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = request.Name!.Trim(),
            Description = request.Description!.Trim(),
            Price = decimal.Round(request.Price!.Value, 2, MidpointRounding.AwayFromZero),
            Status = ProductStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return product.ToResponse();
    }

    public async Task<ProductResponse> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(product => product.Id == id, ct);

        if (product is null)
        {
            throw new NotFoundException("Product", id);
        }

        if (request.Name is not null)
        {
            product.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            product.Description = request.Description.Trim();
        }

        if (request.Price is not null)
        {
            product.Price = decimal.Round(request.Price.Value, 2, MidpointRounding.AwayFromZero);
        }

        if (request.Status is not null)
        {
            product.Status = request.Status.Value;
        }

        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return product.ToResponse();
    }

    public async Task<ProductResponse> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(product => product.Id == id, ct);

        if (product is null)
        {
            throw new NotFoundException("Product", id);
        }

        if (product.Status != ProductStatus.Archived)
        {
            product.Status = ProductStatus.Archived;
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return product.ToResponse();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(product => product.Id == id, ct);

        if (product is null)
        {
            throw new NotFoundException("Product", id);
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
    }
}

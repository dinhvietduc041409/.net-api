using DotnetApi.Contracts;
using DotnetApi.Infrastructure;
using DotnetApi.Interfaces;

namespace DotnetApi.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListProducts")
            .Produces<IReadOnlyList<ProductResponse>>();

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetProduct")
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateProduct")
            .RequireAuthorization(AuthPolicies.ManagerOrAdmin)
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateProduct")
            .RequireAuthorization(AuthPolicies.ManagerOrAdmin)
            .Produces<ProductResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("PatchProduct")
            .RequireAuthorization(AuthPolicies.ManagerOrAdmin)
            .Produces<ProductResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/archive", ArchiveAsync)
            .WithName("ArchiveProduct")
            .RequireAuthorization(AuthPolicies.ManagerOrAdmin)
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteProduct")
            .RequireAuthorization(AuthPolicies.ManagerOrAdmin)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(IProductService products, CancellationToken ct) =>
        Results.Ok(await products.ListAsync(ct));

    private static async Task<IResult> GetAsync(
        Guid id,
        IProductService products,
        CancellationToken ct) =>
        Results.Ok(await products.GetAsync(id, ct));

    private static async Task<IResult> CreateAsync(
        CreateProductRequest request,
        IProductService products,
        CancellationToken ct)
    {
        var errors = RequestValidation.Validate(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var product = await products.CreateAsync(request, ct);
        return Results.Created($"/api/products/{product.Id}", product);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        IProductService products,
        CancellationToken ct)
    {
        var errors = RequestValidation.Validate(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return Results.Ok(await products.UpdateAsync(id, request, ct));
    }

    private static async Task<IResult> ArchiveAsync(
        Guid id,
        IProductService products,
        CancellationToken ct) =>
        Results.Ok(await products.ArchiveAsync(id, ct));

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IProductService products,
        CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return Results.NoContent();
    }
}

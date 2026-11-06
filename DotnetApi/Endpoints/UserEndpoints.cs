using DotnetApi.Contracts;
using DotnetApi.Infrastructure;
using DotnetApi.Interfaces;

namespace DotnetApi.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListUsers")
            .Produces<IReadOnlyList<UserResponse>>();

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetUser")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateUser")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateUser")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("PatchUser")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteUser")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(IUserService users, CancellationToken ct) =>
        Results.Ok(await users.ListAsync(ct));

    private static async Task<IResult> GetAsync(
        Guid id,
        IUserService users,
        CancellationToken ct) =>
        Results.Ok(await users.GetAsync(id, ct));

    private static async Task<IResult> CreateAsync(
        CreateUserRequest request,
        IUserService users,
        CancellationToken ct)
    {
        var errors = RequestValidation.Validate(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = await users.CreateAsync(request, ct);
        return Results.Created($"/api/users/{user.Id}", user);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        IUserService users,
        CancellationToken ct)
    {
        var errors = RequestValidation.Validate(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return Results.Ok(await users.UpdateAsync(id, request, ct));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IUserService users,
        CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return Results.NoContent();
    }
}

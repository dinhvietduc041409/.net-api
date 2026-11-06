using System.Security.Claims;
using DotnetApi.Contracts;
using DotnetApi.Infrastructure;
using DotnetApi.Interfaces;

namespace DotnetApi.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .AllowAnonymous()
            .Produces<AuthTokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshToken")
            .AllowAnonymous()
            .Produces<AuthTokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapGet("/me", GetCurrentUserAsync)
            .WithName("GetCurrentUser")
            .RequireAuthorization()
            .Produces<AuthUserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAuthService auth,
        CancellationToken ct)
    {
        var errors = RequestValidation.Validate(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return Results.Ok(await auth.LoginAsync(request, ct));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshTokenRequest request,
        IAuthService auth,
        CancellationToken ct)
    {
        var errors = RequestValidation.Validate(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return Results.Ok(await auth.RefreshAsync(request.RefreshToken!, ct));
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest request,
        IAuthService auth,
        CancellationToken ct)
    {
        var errors = RequestValidation.Validate(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await auth.LogoutAsync(request.RefreshToken!, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        IAuthService auth,
        CancellationToken ct)
    {
        var userIdValue = principal.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw new InvalidCredentialsException("The access token does not identify a user.");
        }

        return Results.Ok(await auth.GetCurrentUserAsync(userId, ct));
    }
}

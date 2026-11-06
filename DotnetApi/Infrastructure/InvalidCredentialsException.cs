namespace DotnetApi.Infrastructure;

public sealed class InvalidCredentialsException(string detail = "Invalid email or password.")
    : ApiException("Unauthorized", detail, StatusCodes.Status401Unauthorized);

namespace DotnetApi.Infrastructure;

public sealed class ConflictException(string detail)
    : ApiException("Conflict", detail, StatusCodes.Status409Conflict);

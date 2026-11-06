namespace DotnetApi.Infrastructure;

public sealed class NotFoundException(string resource, Guid id)
    : ApiException(
        $"{resource} not found",
        $"{resource} with id '{id}' does not exist.",
        StatusCodes.Status404NotFound);

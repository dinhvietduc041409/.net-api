namespace DotnetApi.Infrastructure;

/// <summary>
/// Base class for expected API failures that should be rendered as ProblemDetails.
/// </summary>
public abstract class ApiException(string title, string detail, int statusCode) : Exception(detail)
{
    public string Title { get; } = title;

    public int StatusCode { get; } = statusCode;

    public string Detail => Message;
}

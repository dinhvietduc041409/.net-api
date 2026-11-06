using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DotnetApi.Infrastructure;

/// <summary>
/// Converts malformed JSON / invalid enum values into a 400 response instead of
/// letting the exception handler turn them into a 500.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var (statusCode, title, detail) = exception switch
        {
            ApiException apiException => (
                apiException.StatusCode,
                apiException.Title,
                apiException.Detail),
            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                "Invalid request",
                GetBadRequestDetail(badRequest)),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                "An unexpected error occurred."),
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning("Rejected request for {Method} {Path}: {Message}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path,
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }

    private static string GetBadRequestDetail(BadHttpRequestException exception) =>
        exception.InnerException is JsonException
            ? "The request body is invalid JSON or contains an unsupported value."
            : exception.StatusCode switch
            {
                StatusCodes.Status413PayloadTooLarge => "The request body is too large.",
                _ => "The request is invalid. Check the request body and parameters.",
            };
}

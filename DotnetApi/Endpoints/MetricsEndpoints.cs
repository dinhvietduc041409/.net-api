using System.Globalization;
using DotnetApi.Contracts;
using DotnetApi.Models;
using DotnetApi.Services;

namespace DotnetApi.Endpoints;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metrics-dashboard")
            .WithTags("Metrics dashboard")
            .RequireAuthorization();

        group.MapGet("/", GetDashboardAsync)
            .WithName("GetMetricsDashboard")
            .Produces<MetricsDashboardResponse>();

        group.MapGet("/kpis", GetKpisAsync)
            .WithName("GetMetricsKpis")
            .Produces<MetricsKpisResponse>();

        group.MapGet("/chart", GetChartAsync)
            .WithName("GetMetricsChart")
            .Produces<MetricsChartResponse>();

        group.MapGet("/logs", GetLogsAsync)
            .WithName("GetMetricsLogs")
            .Produces<MetricsLogsResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> GetDashboardAsync(
        string? range,
        MetricsService metrics,
        CancellationToken ct)
    {
        if (!TryResolveRange(range, out var resolvedRange, out var error))
        {
            return BadRequest(error);
        }

        return Results.Ok(await metrics.GetDashboardAsync(resolvedRange, ct));
    }

    private static async Task<IResult> GetKpisAsync(
        string? range,
        MetricsService metrics,
        CancellationToken ct)
    {
        if (!TryResolveRange(range, out var resolvedRange, out var error))
        {
            return BadRequest(error);
        }

        return Results.Ok(await metrics.GetKpisAsync(resolvedRange, ct));
    }

    private static async Task<IResult> GetChartAsync(
        string? range,
        MetricsService metrics,
        CancellationToken ct)
    {
        if (!TryResolveRange(range, out var resolvedRange, out var error))
        {
            return BadRequest(error);
        }

        return Results.Ok(await metrics.GetChartAsync(resolvedRange, ct));
    }

    private static async Task<IResult> GetLogsAsync(
        string? range,
        string? level,
        int? limit,
        string? cursor,
        MetricsService metrics,
        CancellationToken ct)
    {
        if (!TryResolveRange(range, out var resolvedRange, out var rangeError))
        {
            return BadRequest(rangeError);
        }

        var resolvedLevel = string.IsNullOrEmpty(level) ? "all" : level;

        if (resolvedLevel != "all" && !MetricsLogLevel.IsValid(resolvedLevel))
        {
            return BadRequest(
                $"Invalid \"level\" value. Expected one of: all, {string.Join(", ", MetricsLogLevel.All)}.");
        }

        var resolvedLimit = limit ?? MetricsService.DefaultLogLimit;

        if (resolvedLimit is < 1 or > MetricsService.MaxLogLimit)
        {
            return BadRequest(
                $"Invalid \"limit\" value. Expected an integer between 1 and {MetricsService.MaxLogLimit}.");
        }

        var offset = 0;

        if (!string.IsNullOrEmpty(cursor) &&
            (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0))
        {
            return BadRequest("Invalid \"cursor\" value. Expected a non-negative integer offset.");
        }

        return Results.Ok(await metrics.GetLogsAsync(
            resolvedRange,
            resolvedLevel,
            resolvedLimit,
            offset,
            ct));
    }

    private static bool TryResolveRange(
        string? range,
        out string resolvedRange,
        out string error)
    {
        if (string.IsNullOrEmpty(range))
        {
            resolvedRange = MetricsRanges.Default;
            error = string.Empty;
            return true;
        }

        if (MetricsRanges.IsValid(range))
        {
            resolvedRange = range;
            error = string.Empty;
            return true;
        }

        resolvedRange = MetricsRanges.Default;
        error = $"Invalid \"range\" value. Expected one of: {string.Join(", ", MetricsRanges.All)}.";
        return false;
    }

    private static IResult BadRequest(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
}

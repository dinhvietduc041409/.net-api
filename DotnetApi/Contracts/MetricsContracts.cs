using System.Text.Json.Serialization;

namespace DotnetApi.Contracts;

public sealed record MetricsKpiDetail(
    string Label,
    decimal Value,
    string Format);

public sealed record MetricsKpi(
    string Id,
    string Label,
    decimal Value,
    string Format,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Currency,
    decimal Change,
    string Trend,
    string ComparisonLabel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MetricsKpiDetail>? Details);

public sealed record MetricsChartPoint(
    string Timestamp,
    string Label,
    decimal Value);

public sealed record MetricsChartSeries(
    string Id,
    string Label,
    string Format,
    IReadOnlyList<MetricsChartPoint> Points);

public sealed record MetricsChart(
    string Range,
    string Kind,
    IReadOnlyList<MetricsChartSeries> Series);

public sealed record MetricsKpisResponse(
    DateTimeOffset GeneratedAt,
    string Range,
    IReadOnlyList<MetricsKpi> Kpis);

public sealed record MetricsChartResponse(
    DateTimeOffset GeneratedAt,
    string Range,
    MetricsChart Chart);

public sealed record MetricsLogEntry(
    string Id,
    string Timestamp,
    string Level,
    string Message,
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Actor);

public sealed record MetricsLogsResponse(
    DateTimeOffset GeneratedAt,
    string Range,
    IReadOnlyList<MetricsLogEntry> Entries,
    int Total,
    string? NextCursor);

public sealed record MetricsDashboardResponse(
    DateTimeOffset GeneratedAt,
    string Range,
    IReadOnlyList<MetricsKpi> Kpis,
    MetricsChart Chart,
    MetricsLogsResponse Logs);

public static class MetricsRanges
{
    public const string Last24Hours = "24h";
    public const string Last7Days = "7d";
    public const string Last30Days = "30d";
    public const string Last90Days = "90d";
    public const string Default = Last7Days;

    public static readonly string[] All =
        [Last24Hours, Last7Days, Last30Days, Last90Days];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

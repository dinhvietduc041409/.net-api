namespace DotnetApi.Models;

public sealed class MetricLog
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = MetricsLogLevel.Info;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Actor { get; set; }
}

public static class MetricsLogLevel
{
    public const string Debug = "debug";
    public const string Info = "info";
    public const string Warn = "warn";
    public const string Error = "error";

    public static readonly string[] All = [Debug, Info, Warn, Error];

    public static bool IsValid(string? value) =>
        value is Debug or Info or Warn or Error;
}

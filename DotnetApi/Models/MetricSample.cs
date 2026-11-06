namespace DotnetApi.Models;

public sealed class MetricSample
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Series { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public static class MetricSeries
{
    public const string Users = "users";
    public const string Orders = "orders";
    public const string Revenue = "revenue";

    public static readonly string[] All = [Users, Orders, Revenue];
}

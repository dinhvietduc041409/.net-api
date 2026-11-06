using System.Globalization;
using DotnetApi.Contracts;
using DotnetApi.Data;
using DotnetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DotnetApi.Services;

public sealed class MetricsService(AppDbContext db)
{
    public const int DefaultLogLimit = 10;
    public const int MaxLogLimit = 50;

    private sealed record RangeConfig(
        string Range,
        TimeSpan BucketSize,
        TimeSpan WindowLength,
        int Points,
        string ComparisonLabel);

    private sealed record MetricsBucket(DateTimeOffset Start, DateTimeOffset End);

    private sealed record Snapshot(
        DateTimeOffset GeneratedAt,
        string Range,
        IReadOnlyList<MetricsKpi> Kpis,
        MetricsChart Chart);

    private static readonly IReadOnlyDictionary<string, RangeConfig> Configs =
        new Dictionary<string, RangeConfig>(StringComparer.Ordinal)
        {
            [MetricsRanges.Last24Hours] = new(
                MetricsRanges.Last24Hours,
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(24),
                24,
                "vs previous 24 hours"),
            [MetricsRanges.Last7Days] = new(
                MetricsRanges.Last7Days,
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(7),
                7,
                "vs previous 7 days"),
            [MetricsRanges.Last30Days] = new(
                MetricsRanges.Last30Days,
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(30),
                30,
                "vs previous 30 days"),
            [MetricsRanges.Last90Days] = new(
                MetricsRanges.Last90Days,
                TimeSpan.FromDays(7),
                TimeSpan.FromDays(90),
                13,
                "vs previous 90 days"),
        };

    public async Task<MetricsDashboardResponse> GetDashboardAsync(
        string range,
        CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(range, ct);
        var logs = await GetLogsAsync(range, "all", DefaultLogLimit, 0, ct);

        return new MetricsDashboardResponse(
            snapshot.GeneratedAt,
            snapshot.Range,
            snapshot.Kpis,
            snapshot.Chart,
            logs);
    }

    public async Task<MetricsKpisResponse> GetKpisAsync(string range, CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(range, ct);

        return new MetricsKpisResponse(snapshot.GeneratedAt, snapshot.Range, snapshot.Kpis);
    }

    public async Task<MetricsChartResponse> GetChartAsync(string range, CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(range, ct);

        return new MetricsChartResponse(snapshot.GeneratedAt, snapshot.Range, snapshot.Chart);
    }

    public async Task<MetricsLogsResponse> GetLogsAsync(
        string range,
        string level,
        int limit,
        int offset,
        CancellationToken ct)
    {
        var config = GetConfig(range);
        var now = DateTimeOffset.UtcNow;
        var rangeStart = now - config.WindowLength;

        var query = db.MetricLogs.AsNoTracking()
            .Where(log => log.Timestamp >= rangeStart && log.Timestamp <= now);

        if (!string.Equals(level, "all", StringComparison.Ordinal))
        {
            query = query.Where(log => log.Level == level);
        }

        var total = await query.CountAsync(ct);

        var logs = await query
            .OrderByDescending(log => log.Timestamp)
            .ThenByDescending(log => log.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        var entries = logs
            .Select(log => new MetricsLogEntry(
                log.Id.ToString(),
                log.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                log.Level,
                log.Message,
                log.Source,
                log.Actor))
            .ToList();

        var nextOffset = offset + entries.Count;
        var nextCursor = nextOffset < total
            ? nextOffset.ToString(CultureInfo.InvariantCulture)
            : null;

        return new MetricsLogsResponse(now, config.Range, entries, total, nextCursor);
    }

    private async Task<Snapshot> GetSnapshotAsync(string range, CancellationToken ct)
    {
        var config = GetConfig(range);
        var now = DateTimeOffset.UtcNow;
        var buckets = BuildBuckets(config, now);

        var windowStart = buckets[0].Start;
        var windowEnd = buckets[^1].End;
        var previousStart = windowStart - config.WindowLength;

        var samples = await db.MetricSamples.AsNoTracking()
            .Where(sample => sample.Timestamp >= previousStart && sample.Timestamp < windowEnd)
            .OrderBy(sample => sample.Timestamp)
            .ToListAsync(ct);

        var users = new decimal[config.Points];
        var orders = new decimal[config.Points];
        var revenue = new decimal[config.Points];
        var hasUsers = new bool[config.Points];

        decimal previousUsers = 0;
        decimal previousOrders = 0;
        decimal previousRevenue = 0;

        foreach (var sample in samples)
        {
            if (sample.Timestamp < windowStart)
            {
                switch (sample.Series)
                {
                    case MetricSeries.Users:
                        previousUsers = sample.Value;
                        break;
                    case MetricSeries.Orders:
                        previousOrders += sample.Value;
                        break;
                    case MetricSeries.Revenue:
                        previousRevenue += sample.Value;
                        break;
                }

                continue;
            }

            var index = BucketIndex(sample.Timestamp, buckets);

            if (index < 0)
            {
                continue;
            }

            switch (sample.Series)
            {
                case MetricSeries.Users:
                    users[index] = sample.Value;
                    hasUsers[index] = true;
                    break;
                case MetricSeries.Orders:
                    orders[index] += sample.Value;
                    break;
                case MetricSeries.Revenue:
                    revenue[index] += sample.Value;
                    break;
            }
        }

        var carry = previousUsers;

        for (var index = 0; index < config.Points; index++)
        {
            if (hasUsers[index])
            {
                carry = users[index];
            }
            else
            {
                users[index] = carry;
            }
        }

        var currentUsers = users[^1];
        var firstUsers = users[0];
        var currentOrders = orders.Sum();
        var currentRevenue = revenue.Sum();

        var activeUsers = decimal.Round(currentUsers * 0.68m, 0, MidpointRounding.AwayFromZero);
        var newUsers = decimal.Max(0, currentUsers - firstUsers);
        var returningUsers = decimal.Round(activeUsers * 0.74m, 0, MidpointRounding.AwayFromZero);
        var pendingOrders = decimal.Round(currentOrders * 0.14m, 0, MidpointRounding.AwayFromZero);
        var completedOrders = decimal.Max(0, currentOrders - pendingOrders);
        var averageOrderValue = currentOrders > 0
            ? decimal.Round(currentRevenue / currentOrders, 2, MidpointRounding.AwayFromZero)
            : 0;
        var refunds = decimal.Round(currentRevenue * 0.012m, 2, MidpointRounding.AwayFromZero);

        var usersChange = CalculateChange(previousUsers, currentUsers);
        var ordersChange = CalculateChange(previousOrders, currentOrders);
        var revenueChange = CalculateChange(previousRevenue, currentRevenue);
        var comparisonLabel = config.ComparisonLabel;

        var kpis = new List<MetricsKpi>
        {
            new(
                "users",
                "Users",
                RoundZero(currentUsers),
                "number",
                null,
                usersChange,
                TrendFromChange(usersChange),
                comparisonLabel,
                [
                    new MetricsKpiDetail("Active", activeUsers, "number"),
                    new MetricsKpiDetail("New", newUsers, "number"),
                ]),
            new(
                "active-users",
                "Active users",
                activeUsers,
                "number",
                null,
                usersChange,
                TrendFromChange(usersChange),
                comparisonLabel,
                [
                    new MetricsKpiDetail("Returning", returningUsers, "number"),
                    new MetricsKpiDetail("New", newUsers, "number"),
                ]),
            new(
                "orders",
                "Orders",
                RoundZero(currentOrders),
                "number",
                null,
                ordersChange,
                TrendFromChange(ordersChange),
                comparisonLabel,
                [
                    new MetricsKpiDetail("Pending", pendingOrders, "number"),
                    new MetricsKpiDetail("Completed", completedOrders, "number"),
                ]),
            new(
                "revenue",
                "Revenue",
                RoundTwo(currentRevenue),
                "currency",
                "USD",
                revenueChange,
                TrendFromChange(revenueChange),
                comparisonLabel,
                [
                    new MetricsKpiDetail("Avg. order", averageOrderValue, "currency"),
                    new MetricsKpiDetail("Refunds", refunds, "currency"),
                ]),
        };

        var chart = new MetricsChart(
            config.Range,
            "area",
            [
                BuildSeries("users", "Users", "number", users, buckets, config, 0),
                BuildSeries("orders", "Orders", "number", orders, buckets, config, 0),
                BuildSeries("revenue", "Revenue", "currency", revenue, buckets, config, 2),
            ]);

        return new Snapshot(now, config.Range, kpis, chart);
    }

    private static RangeConfig GetConfig(string range) =>
        Configs.TryGetValue(range, out var config)
            ? config
            : throw new ArgumentOutOfRangeException(nameof(range), range, "Unsupported metrics range.");

    private static List<MetricsBucket> BuildBuckets(RangeConfig config, DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        var currentBucketStart = config.BucketSize < TimeSpan.FromDays(1)
            ? new DateTimeOffset(
                utcNow.Year,
                utcNow.Month,
                utcNow.Day,
                utcNow.Hour,
                0,
                0,
                TimeSpan.Zero)
            : new DateTimeOffset(
                utcNow.Year,
                utcNow.Month,
                utcNow.Day,
                0,
                0,
                0,
                TimeSpan.Zero);

        // The range ends at the end of the current hour/day. Multi-day buckets
        // must not push that boundary into the following week.
        var rangeEnd = config.BucketSize < TimeSpan.FromDays(1)
            ? currentBucketStart + config.BucketSize
            : currentBucketStart + TimeSpan.FromDays(1);
        var rangeStart = rangeEnd - config.WindowLength;

        var buckets = new List<MetricsBucket>(config.Points);
        var bucketEnd = rangeEnd;

        while (buckets.Count < config.Points)
        {
            var bucketStart = bucketEnd - config.BucketSize;

            if (bucketStart < rangeStart)
            {
                bucketStart = rangeStart;
            }

            buckets.Add(new MetricsBucket(bucketStart, bucketEnd));
            bucketEnd = bucketStart;

            if (bucketStart == rangeStart)
            {
                break;
            }
        }

        buckets.Reverse();
        return buckets;
    }

    private static int BucketIndex(
        DateTimeOffset timestamp,
        IReadOnlyList<MetricsBucket> buckets)
    {
        for (var index = 0; index < buckets.Count; index++)
        {
            if (timestamp >= buckets[index].Start && timestamp < buckets[index].End)
            {
                return index;
            }
        }

        return -1;
    }

    private static MetricsChartSeries BuildSeries(
        string id,
        string label,
        string format,
        IReadOnlyList<decimal> values,
        IReadOnlyList<MetricsBucket> buckets,
        RangeConfig config,
        int decimals)
    {
        var points = new List<MetricsChartPoint>(buckets.Count);

        for (var index = 0; index < buckets.Count; index++)
        {
            var value = decimals == 0
                ? RoundZero(values[index])
                : RoundTwo(values[index]);

            points.Add(new MetricsChartPoint(
                buckets[index].Start.ToString("O", CultureInfo.InvariantCulture),
                FormatBucketLabel(buckets[index].Start, config),
                value));
        }

        return new MetricsChartSeries(id, label, format, points);
    }

    private static string FormatBucketLabel(DateTimeOffset timestamp, RangeConfig config) =>
        config.BucketSize < TimeSpan.FromDays(1)
            ? timestamp.ToString("HH:mm", CultureInfo.InvariantCulture)
            : timestamp.ToString("MMM d", CultureInfo.InvariantCulture);

    private static decimal CalculateChange(decimal previous, decimal current)
    {
        if (previous == 0)
        {
            return current == 0 ? 0 : 100;
        }

        return decimal.Round(
            ((current - previous) / previous) * 100m,
            1,
            MidpointRounding.AwayFromZero);
    }

    private static string TrendFromChange(decimal change) =>
        change > 0.5m ? "up" : change < -0.5m ? "down" : "flat";

    private static decimal RoundZero(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    private static decimal RoundTwo(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

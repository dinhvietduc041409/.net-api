using DotnetApi.Infrastructure;
using DotnetApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DotnetApi.Data;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync(ct);

        await SeedData.InitializeAsync(db, passwordHasher, logger, ct);
    }
}

public static class SeedData
{
    private static readonly (string Name, string Email, UserRole Role, int CreatedDaysAgo)[] Users =
    [
        ("John Doe", "john@example.com", UserRole.Admin, 180),
        ("Jane Smith", "jane@example.com", UserRole.Manager, 172),
        ("Bob Johnson", "bob@example.com", UserRole.Viewer, 164),
        ("Alice Chen", "alice@example.com", UserRole.Admin, 150),
        ("Marcus Lee", "marcus@example.com", UserRole.Manager, 138),
        ("Priya Patel", "priya@example.com", UserRole.Viewer, 125),
        ("Tom Becker", "tom@example.com", UserRole.Manager, 110),
        ("Sofia Rossi", "sofia@example.com", UserRole.Viewer, 96),
        ("Daniel Kim", "daniel@example.com", UserRole.Viewer, 78),
        ("Nina Park", "nina@example.com", UserRole.Manager, 60),
        ("Omar Haddad", "omar@example.com", UserRole.Viewer, 42),
        ("Lena Vogel", "lena@example.com", UserRole.Admin, 25),
    ];

    private static readonly (string Name, string Description, decimal Price, ProductStatus Status, int CreatedDaysAgo)[] Products =
    [
        ("Aurora Desk Lamp", "Warm dimmable task light with a steel base.", 89m, ProductStatus.Active, 170),
        ("Atlas Standing Desk", "Height-adjustable desk with a compact footprint.", 649m, ProductStatus.Active, 162),
        ("Cove Task Chair", "Breathable mesh chair with adjustable lumbar support.", 329m, ProductStatus.Active, 148),
        ("Drift Monitor Arm", "Gas-adjustable monitor arm with integrated cable management.", 129m, ProductStatus.Active, 135),
        ("Ember Notebook", "Lay-flat notebook with dot-grid pages and a linen cover.", 18m, ProductStatus.Active, 120),
        ("Field Backpack", "Weather-resistant commuter pack with a padded laptop sleeve.", 74m, ProductStatus.Archived, 108),
        ("Grove Side Table", "Solid oak side table with a rounded edge profile.", 219m, ProductStatus.Active, 92),
        ("Halo Floor Lamp", "Arc floor lamp with a diffused linen shade.", 349m, ProductStatus.Active, 76),
        ("Iris Storage Box", "Stackable felt storage box with a reinforced handle.", 32m, ProductStatus.Archived, 55),
        ("Juno Wall Clock", "Silent sweep wall clock with a brushed aluminium rim.", 58m, ProductStatus.Active, 32),
        ("Kite Desk Pad", "Full-grain leather desk pad with a cork backing.", 96m, ProductStatus.Active, 18),
        ("Loom Throw Blanket", "Merino-blend throw blanket with a herringbone weave.", 142m, ProductStatus.Active, 6),
    ];

    private static readonly (string Level, string Message, string Source, string? Actor)[] LogTemplates =
    [
        (MetricsLogLevel.Info, "User invitation accepted", "users-remote", "jane@example.com"),
        (MetricsLogLevel.Info, "New order created", "orders-service", "john@example.com"),
        (MetricsLogLevel.Warn, "Payment retry scheduled", "billing-service", "system"),
        (MetricsLogLevel.Error, "Webhook delivery failed", "integrations", "system"),
        (MetricsLogLevel.Debug, "Cache refreshed for tenant", "admin-shell", null),
        (MetricsLogLevel.Info, "Role updated for user", "rbac", "admin@example.com"),
        (MetricsLogLevel.Warn, "Rate limit approaching for API key", "api-gateway", null),
        (MetricsLogLevel.Error, "Order export timed out", "reports", null),
        (MetricsLogLevel.Info, "Revenue report generated", "reports", "manager@example.com"),
        (MetricsLogLevel.Debug, "Session token refreshed", "auth-next", null),
    ];

    public static async Task InitializeAsync(
        AppDbContext db,
        IPasswordHasher<AppUser> passwordHasher,
        ILogger logger,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var hasChanges = false;

        if (!await db.Users.AnyAsync(ct))
        {
            db.Users.AddRange(Users.Select(user =>
            {
                var appUser = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = user.Email,
                    Name = user.Name,
                    Role = user.Role,
                    CreatedAt = now.AddDays(-user.CreatedDaysAgo),
                    UpdatedAt = now.AddDays(-user.CreatedDaysAgo).AddDays(3),
                };

                appUser.PasswordHash = passwordHasher.HashPassword(
                    appUser,
                    AuthDefaults.DemoPassword);

                return appUser;
            }));

            hasChanges = true;
        }

        var usersWithoutPassword = await db.Users
            .Where(user => user.PasswordHash == string.Empty)
            .ToListAsync(ct);

        foreach (var user in usersWithoutPassword)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, AuthDefaults.DemoPassword);
        }

        if (usersWithoutPassword.Count > 0)
        {
            hasChanges = true;
            logger.LogInformation(
                "Backfilled demo passwords for {Count} existing users.",
                usersWithoutPassword.Count);
        }

        if (!await db.Products.AnyAsync(ct))
        {
            db.Products.AddRange(Products.Select(product => new Product
            {
                Id = Guid.NewGuid(),
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Status = product.Status,
                CreatedAt = now.AddDays(-product.CreatedDaysAgo),
                UpdatedAt = now.AddDays(-product.CreatedDaysAgo).AddDays(1),
            }));

            hasChanges = true;
        }

        if (!await db.MetricSamples.AnyAsync(ct))
        {
            db.MetricSamples.AddRange(CreateMetricSamples(now));
            hasChanges = true;
        }

        if (!await db.MetricLogs.AnyAsync(ct))
        {
            db.MetricLogs.AddRange(CreateMetricLogs(now));
            hasChanges = true;
        }

        if (!hasChanges)
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Seed data was already created by another instance.");
            return;
        }

        logger.LogInformation("Seed data created for users, products, metric samples and logs.");
    }

    private static IEnumerable<MetricSample> CreateMetricSamples(DateTimeOffset now)
    {
        const int days = 180;
        const int totalHours = days * 24;
        var utcNow = now.ToUniversalTime();
        var currentHour = new DateTimeOffset(
            utcNow.Year,
            utcNow.Month,
            utcNow.Day,
            utcNow.Hour,
            0,
            0,
            TimeSpan.Zero);
        var start = currentHour.AddDays(-days);
        var samples = new List<MetricSample>(totalHours * 3);

        for (var hour = 0; hour < totalHours; hour++)
        {
            var timestamp = start.AddHours(hour);
            var progress = hour / (double)(totalHours - 1);
            var hourOfDay = timestamp.Hour;

            var usersValue = 8_600d
                + 3_800d * progress
                + 160d * Math.Sin(hourOfDay / 24d * Math.PI * 2)
                + 50d * (Noise(hour + 11) - 0.5);

            var ordersValue = 8d
                + 3d * Math.Sin((hourOfDay - 8) / 24d * Math.PI * 2)
                + 2d * (Noise(hour + 23) - 0.5);
            ordersValue = Math.Max(0, Math.Round(ordersValue, 0));

            var averageOrderValue = 115d + 30d * (Noise(hour + 37) - 0.5);
            var revenueValue = Math.Round(ordersValue * averageOrderValue, 2);

            samples.Add(new MetricSample
            {
                Id = Guid.NewGuid(),
                Timestamp = timestamp,
                Series = MetricSeries.Users,
                Value = decimal.Round((decimal)usersValue, 0, MidpointRounding.AwayFromZero),
            });

            samples.Add(new MetricSample
            {
                Id = Guid.NewGuid(),
                Timestamp = timestamp,
                Series = MetricSeries.Orders,
                Value = (decimal)ordersValue,
            });

            samples.Add(new MetricSample
            {
                Id = Guid.NewGuid(),
                Timestamp = timestamp,
                Series = MetricSeries.Revenue,
                Value = (decimal)revenueValue,
            });
        }

        return samples;
    }

    private static IEnumerable<MetricLog> CreateMetricLogs(DateTimeOffset now)
    {
        const int totalMinutes = 180 * 24 * 60;
        const int count = 420;
        var logs = new List<MetricLog>(count);

        for (var index = 0; index < count; index++)
        {
            var template = LogTemplates[index % LogTemplates.Length];
            var progress = index / (double)count;
            var offsetMinutes = progress * totalMinutes + Noise(index + 71) * 120;
            var timestamp = now.AddMinutes(-offsetMinutes).AddSeconds(-(index % 60));

            logs.Add(new MetricLog
            {
                Id = Guid.NewGuid(),
                Timestamp = timestamp,
                Level = template.Level,
                Message = template.Message,
                Source = template.Source,
                Actor = template.Actor,
            });
        }

        return logs;
    }

    private static double Noise(int seed)
    {
        var value = Math.Sin(seed * 12.9898) * 43_758.5453;
        return value - Math.Floor(value);
    }
}

using DotnetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DotnetApi.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<MetricLog> MetricLogs => Set<MetricLog>();
    public DbSet<MetricSample> MetricSamples => Set<MetricSample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users", table =>
                table.HasCheckConstraint(
                    "ck_users_role",
                    "role IN ('admin', 'manager', 'viewer')"));
            entity.HasKey(user => user.Id);

            entity.Property(user => user.Id).HasColumnName("id");
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            entity.Property(user => user.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(user => user.PasswordHash)
                .HasColumnName("password_hash")
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(user => user.Role)
                .HasColumnName("role")
                .HasMaxLength(20)
                .HasConversion(
                    role => role == UserRole.Admin
                        ? "admin"
                        : role == UserRole.Manager
                            ? "manager"
                            : "viewer",
                    value => value == "admin"
                        ? UserRole.Admin
                        : value == "manager"
                            ? UserRole.Manager
                            : UserRole.Viewer)
                .IsRequired();
            entity.Property(user => user.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ux_users_email");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products", table =>
            {
                table.HasCheckConstraint(
                    "ck_products_status",
                    "status IN ('active', 'archived')");
                table.HasCheckConstraint("ck_products_price", "price >= 0");
            });

            entity.HasKey(product => product.Id);

            entity.Property(product => product.Id).HasColumnName("id");
            entity.Property(product => product.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(product => product.Description).HasColumnName("description").IsRequired();
            entity.Property(product => product.Price).HasColumnName("price").HasPrecision(12, 2).IsRequired();
            entity.Property(product => product.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .HasConversion(
                    status => status == ProductStatus.Active ? "active" : "archived",
                    value => value == "active" ? ProductStatus.Active : ProductStatus.Archived)
                .IsRequired();
            entity.Property(product => product.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(product => product.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(product => product.Status).HasDatabaseName("ix_products_status");
        });

        modelBuilder.Entity<MetricLog>(entity =>
        {
            entity.ToTable("metric_logs", table =>
                table.HasCheckConstraint(
                    "ck_metric_logs_level",
                    "level IN ('debug', 'info', 'warn', 'error')"));

            entity.HasKey(log => log.Id);

            entity.Property(log => log.Id).HasColumnName("id");
            entity.Property(log => log.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(log => log.Level).HasColumnName("level").HasMaxLength(10).IsRequired();
            entity.Property(log => log.Message).HasColumnName("message").IsRequired();
            entity.Property(log => log.Source).HasColumnName("source").HasMaxLength(100).IsRequired();
            entity.Property(log => log.Actor).HasColumnName("actor").HasMaxLength(200);

            entity.HasIndex(log => log.Timestamp).HasDatabaseName("ix_metric_logs_timestamp");
            entity.HasIndex(log => log.Level).HasDatabaseName("ix_metric_logs_level");
        });

        modelBuilder.Entity<MetricSample>(entity =>
        {
            entity.ToTable("metric_samples", table =>
                table.HasCheckConstraint(
                    "ck_metric_samples_series",
                    "series IN ('users', 'orders', 'revenue')"));

            entity.HasKey(sample => sample.Id);

            entity.Property(sample => sample.Id).HasColumnName("id");
            entity.Property(sample => sample.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(sample => sample.Series).HasColumnName("series").HasMaxLength(20).IsRequired();
            entity.Property(sample => sample.Value).HasColumnName("value").HasPrecision(18, 2).IsRequired();

            entity.HasIndex(sample => sample.Timestamp).HasDatabaseName("ix_metric_samples_timestamp");
            entity.HasIndex(sample => new { sample.Series, sample.Timestamp })
                .IsUnique()
                .HasDatabaseName("ux_metric_samples_series_timestamp");
        });
    }
}

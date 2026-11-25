using Microsoft.EntityFrameworkCore;

namespace Nabd.Core;

public sealed class NabdDbContext(DbContextOptions<NabdDbContext> options) : DbContext(options)
{
    public DbSet<Monitor> Monitors => Set<Monitor>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Monitor>().HasIndex(m => m.NextCheckAt);
        model.Entity<CheckResult>().HasIndex(r => new { r.MonitorId, r.At });

        // SQLite round-trips DateTime with Kind=Unspecified; stamp it back to UTC
        // so serialized timestamps keep their Z and clients parse them right.
        var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        model.Entity<Monitor>().Property(m => m.CreatedAt).HasConversion(utc);
        model.Entity<Monitor>().Property(m => m.NextCheckAt).HasConversion(utc);
        model.Entity<CheckResult>().Property(r => r.At).HasConversion(utc);
    }
}

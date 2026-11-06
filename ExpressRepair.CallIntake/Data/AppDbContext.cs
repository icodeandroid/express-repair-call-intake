using ExpressRepair.CallIntake.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpressRepair.CallIntake.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CallRecord> Calls => Set<CallRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var call = modelBuilder.Entity<CallRecord>();

        // The provider's CallSid is our idempotency key: a retried webhook
        // must not create a second row. Enforced in the database, not just
        // in the handler, so a race between two deliveries cannot slip past.
        call.HasIndex(c => c.CallSid).IsUnique();

        // The leads page always reads newest first.
        call.HasIndex(c => c.ReceivedAt);

        call.Property(c => c.CallSid).IsRequired().HasMaxLength(64);
        call.Property(c => c.From).IsRequired().HasMaxLength(32);
        call.Property(c => c.To).HasMaxLength(32);
        call.Property(c => c.RecordingSid).HasMaxLength(64);
        call.Property(c => c.RecordingUrl).HasMaxLength(1024);

        // Stored as text so the database stays readable by eye during the demo.
        call.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
    }
}

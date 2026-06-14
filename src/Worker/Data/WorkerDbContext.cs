using Kendo.Worker.Data;
using Microsoft.EntityFrameworkCore;

namespace Kendo.Worker.Data;

public class WorkerDbContext : DbContext
{
    public WorkerDbContext(DbContextOptions<WorkerDbContext> options) : base(options) { }

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<DlqRecord> DlqRecords => Set<DlqRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.MessageId).ValueGeneratedNever(); // set by application
            entity.Property(e => e.HandlerName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasDefaultValue(IdempotencyStatus.Processing);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.CompletedAt);
        });

        modelBuilder.Entity<DlqRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // set by application
            entity.Property(e => e.OriginalMessageId).IsRequired();
            entity.Property(e => e.OriginalMessageType).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DeadLetterReason).HasMaxLength(500);
            entity.Property(e => e.DeadLetterErrorDescription).HasMaxLength(2000);
            entity.Property(e => e.DetectedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Alerted).HasDefaultValue(false);
            entity.HasIndex(e => e.OriginalMessageId);
            entity.HasIndex(e => e.DetectedAt);
        });
    }
}

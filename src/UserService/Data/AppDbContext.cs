using Kendo.UserService.Models;
using Microsoft.EntityFrameworkCore;

namespace Kendo.UserService.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Email).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasDefaultValue(UserStatus.Pending);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.ProcessedAt);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.MessageId).IsRequired();
            entity.Property(e => e.MessageType).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.ProcessedAt);
            entity.Property(e => e.RetryCount).HasDefaultValue(0);
            entity.Property(e => e.LastError).HasMaxLength(2000);
            entity.Property(e => e.TraceContext).HasMaxLength(100);

            // Filtered index for efficient relay polling
            entity.HasIndex(e => e.CreatedAt)
                .HasFilter("\"ProcessedAt\" IS NULL")
                .HasDatabaseName("IX_OutboxMessages_Unprocessed");

            // Unique index to prevent duplicate message entries
            entity.HasIndex(e => e.MessageId)
                .IsUnique()
                .HasDatabaseName("IX_OutboxMessages_MessageId");
        });
    }
}

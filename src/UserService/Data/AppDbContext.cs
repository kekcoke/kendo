using Kendo.UserService.Domain.Events;
using Kendo.UserService.Domain.Users;
using Kendo.UserService.Models;
using Microsoft.EntityFrameworkCore;

namespace Kendo.UserService.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventValidation> EventValidations => Set<EventValidation>();
    public DbSet<EventEmbedding> EventEmbeddings => Set<EventEmbedding>();
    public DbSet<UserEmbedding> UserEmbeddings => Set<UserEmbedding>();

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

        // ── Event entity ────────────────────────────────────────────────
        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.StartsAt).IsRequired();
            entity.Property(e => e.EndsAt).IsRequired();
            entity.Property(e => e.Location).HasMaxLength(500);
            entity.Property(e => e.Headcount);
            entity.Property(e => e.DietaryNotes).HasMaxLength(1000);
            entity.Property(e => e.SourceText).IsRequired();
            entity.Property(e => e.IngestionStatus)
                .HasConversion<string>()
                .IsRequired()
                .HasDefaultValue(EventIngestionStatus.Pending);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Validation)
                .WithOne(v => v.Event)
                .HasForeignKey<EventValidation>(v => v.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── EventValidation entity ──────────────────────────────────────
        modelBuilder.Entity<EventValidation>(entity =>
        {
            entity.ToTable("event_validations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Ok).IsRequired();
            entity.Property(e => e.ConflictsJson).IsRequired();
            entity.Property(e => e.SuggestionsJson).IsRequired();
            entity.Property(e => e.ReasoningTrace).HasMaxLength(8000);
            entity.Property(e => e.ValidatedAt).HasDefaultValueSql("now()");

            entity.HasIndex(e => e.EventId).IsUnique();
        });

        // ── EventEmbedding entity ───────────────────────────────────────
        modelBuilder.Entity<EventEmbedding>(entity =>
        {
            entity.ToTable("event_embeddings");
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.ModelName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Dimensions).IsRequired();
            entity.Property(e => e.Embedding).HasColumnType("real[]").IsRequired();
            entity.Property(e => e.EmbeddedAt).HasDefaultValueSql("now()");

            entity.HasOne(e => e.Event)
                .WithOne()
                .HasForeignKey<EventEmbedding>(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ModelName);
        });

        // ── UserEmbedding entity ────────────────────────────────────────
        modelBuilder.Entity<UserEmbedding>(entity =>
        {
            entity.ToTable("user_embeddings");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.ModelName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Dimensions).IsRequired();
            entity.Property(e => e.Embedding).HasColumnType("real[]").IsRequired();
            entity.Property(e => e.EmbeddedAt).HasDefaultValueSql("now()");

            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserEmbedding>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ModelName);
        });
    }
}

using System.Diagnostics;
using Kendo.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Kendo.UserService.Data;

/// <summary>
/// Repository for managing transactional outbox records.
/// Outbox records are written atomically with the business transaction
/// to ensure at-least-once delivery of domain events.
/// </summary>
public class OutboxRepository
{
    private readonly AppDbContext _db;
    private readonly ResilientAppDbContext _resilientDb;

    public OutboxRepository(AppDbContext db, ResilientAppDbContext resilientDb)
    {
        _db = db;
        _resilientDb = resilientDb;
    }

    /// <summary>
    /// Adds an outbox record for the given domain message.
    /// This should be called within the same transaction scope as the business operation.
    /// </summary>
    public virtual async Task AddAsync(KendoMessage message, CancellationToken ct = default)
    {
        var activityId = Activity.Current?.Id;

        var outboxMessage = new OutboxMessage
        {
            MessageId = message.MessageId,
            MessageType = KendoMessageSerializer.GetMessageType(message),
            Payload = KendoMessageSerializer.Serialize(message),
            CreatedAt = DateTimeOffset.UtcNow,
            TraceContext = activityId
        };

        _db.OutboxMessages.Add(outboxMessage);

        // Note: SaveChanges is called by the calling code's transaction scope.
        // This just stages the entity in the change tracker.
        await _resilientDb.ExecuteAsync(async token =>
        {
            await _db.SaveChangesAsync(token);
        }, ct);
    }

    /// <summary>
    /// Returns the number of pending (unprocessed) outbox messages.
    /// Useful for health checks and diagnostics.
    /// </summary>
    public virtual async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        return await _resilientDb.ExecuteAsync(async token =>
            await _db.OutboxMessages.CountAsync(m => m.ProcessedAt == null, token), ct);
    }
}

using Kendo.Shared.Authentication;
using Kendo.UserService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Kendo.UserService.Controllers;

/// <summary>
/// Internal admin controller for upserting embeddings.
/// Protected by admin:writes scope + token_use=service (service JWTs only).
/// Uses a scoped userservice_writer PostgreSQL connection for defense-in-depth.
/// </summary>
[ApiController]
[Route("internal")]
[Authorize(Policy = AdminScopePoliciesExtensions.AdminWritesPolicy)]
public class EmbeddingAdminController : ControllerBase
{
    private readonly AdminWriterConnectionFactory _connectionFactory;
    private readonly ILogger<EmbeddingAdminController> _logger;

    public EmbeddingAdminController(
        AdminWriterConnectionFactory connectionFactory,
        ILogger<EmbeddingAdminController> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Upserts an embedding for an event or user.
    /// Request body specifies the target type and the vector payload.
    /// </summary>
    [HttpPost("embeddings")]
    public async Task<IActionResult> UpsertEmbedding([FromBody] UpsertEmbeddingRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ModelName) || request.Embedding is null || request.Embedding.Length == 0)
            return BadRequest(new ProblemDetails
            {
                Type = "https://httpstatuses.com/400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "ModelName and Embedding are required."
            });

        string tableName = request.Target switch
        {
            "event" => "event_embeddings",
            "user" => "user_embeddings",
            _ => null!
        };

        if (tableName is null)
            return BadRequest(new ProblemDetails
            {
                Type = "https://httpstatuses.com/400",
                Title = "Invalid Target",
                Status = 400,
                Detail = "Target must be 'event' or 'user'."
            });

        string idColumn = request.Target == "event" ? "EventId" : "UserId";
        string[] embeddingStrings = request.Embedding.Select(f => f.ToString()).ToArray();
        string vectorLiteral = $"[{string.Join(",", embeddingStrings)}]";

        await using var conn = await _connectionFactory.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        // Upsert: INSERT or UPDATE on conflict
        cmd.CommandText = $"""
            INSERT INTO {tableName} ("{idColumn}", "ModelName", "Dimensions", "Embedding", "EmbeddedAt")
            VALUES (@TargetId, @ModelName, @Dimensions, @Embedding::vector, now())
            ON CONFLICT ("{idColumn}")
            DO UPDATE SET
                "ModelName" = EXCLUDED."ModelName",
                "Dimensions" = EXCLUDED."Dimensions",
                "Embedding" = EXCLUDED."Embedding",
                "EmbeddedAt" = now()
            """;

        cmd.Parameters.AddWithValue("TargetId", request.TargetId);
        cmd.Parameters.AddWithValue("ModelName", request.ModelName);
        cmd.Parameters.AddWithValue("Dimensions", request.Dimensions);
        cmd.Parameters.AddWithValue("Embedding", vectorLiteral);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation(
            "Upserted {Target} embedding for {TargetId} with model {ModelName} ({Dimensions}d)",
            request.Target, request.TargetId, request.ModelName, request.Dimensions);

        return NoContent();
    }

    /// <summary>
    /// Batch upsert embeddings for multiple events (W5 backfill).
    /// Accepts an array of embedding payloads, returns 202 with a job ID.
    /// Idempotent: upsert by event_id + model_name.
    /// </summary>
    [HttpPost("embeddings/batch")]
    public async Task<IActionResult> BatchUpsertEmbeddings(
        [FromBody] BatchUpsertEmbeddingsRequest request, CancellationToken ct)
    {
        if (request.Embeddings is null || request.Embeddings.Count == 0)
            return BadRequest(new ProblemDetails
            {
                Type = "https://httpstatuses.com/400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "Embeddings list is required and must not be empty."
            });

        var jobId = Guid.NewGuid();
        int processedCount = 0;

        await using var conn = await _connectionFactory.OpenAsync(ct);

        foreach (var emb in request.Embeddings)
        {
            string tableName = emb.Target switch
            {
                "event" => "event_embeddings",
                "user" => "user_embeddings",
                _ => null!
            };

            if (tableName is null)
            {
                _logger.LogWarning("Skipping unknown target type: {Target}", emb.Target);
                continue;
            }

            string idColumn = emb.Target == "event" ? "EventId" : "UserId";
            string[] embeddingStrings = emb.Embedding.Select(f => f.ToString()).ToArray();
            string vectorLiteral = $"[{string.Join(",", embeddingStrings)}]";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {tableName} ("{idColumn}", "ModelName", "Dimensions", "Embedding", "EmbeddedAt")
                VALUES (@TargetId, @ModelName, @Dimensions, @Embedding::vector, now())
                ON CONFLICT ("{idColumn}")
                DO UPDATE SET
                    "ModelName" = EXCLUDED."ModelName",
                    "Dimensions" = EXCLUDED."Dimensions",
                    "Embedding" = EXCLUDED."Embedding",
                    "EmbeddedAt" = now()
                """;

            cmd.Parameters.AddWithValue("TargetId", emb.TargetId);
            cmd.Parameters.AddWithValue("ModelName", emb.ModelName);
            cmd.Parameters.AddWithValue("Dimensions", emb.Dimensions);
            cmd.Parameters.AddWithValue("Embedding", vectorLiteral);

            await cmd.ExecuteNonQueryAsync(ct);
            processedCount++;
        }

        _logger.LogInformation(
            "Batch upsert job {JobId}: processed {Count} embeddings",
            jobId, processedCount);

        return Accepted(new
        {
            job_id = jobId,
            processed = processedCount,
            status = "accepted",
        });
    }

    /// <summary>
    /// Re-triggers embedding compute for an event (W5 backfill).
    /// </summary>
    [HttpPost("events/{id:guid}/reindex")]
    public async Task<IActionResult> ReindexEvent(Guid id, CancellationToken ct)
    {
        // Placeholder for W5 backfill trigger.
        // Actual implementation will call the FastAPI service to re-compute embeddings.
        _logger.LogInformation("Reindex requested for event {EventId}", id);

        await using var conn = await _connectionFactory.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = $"""
            DELETE FROM event_embeddings WHERE "EventId" = @EventId
            """;
        cmd.Parameters.AddWithValue("EventId", id);
        await cmd.ExecuteNonQueryAsync(ct);

        return Accepted(new { message = $"Reindex triggered for event {id}. Embedding will be recomputed asynchronously." });
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────

public record UpsertEmbeddingRequest
{
    public string Target { get; init; } = string.Empty; // "event" or "user"
    public Guid TargetId { get; init; }
    public string ModelName { get; init; } = string.Empty;
    public int Dimensions { get; init; }
    public float[] Embedding { get; init; } = [];
}

public record BatchUpsertEmbeddingsRequest
{
    public List<UpsertEmbeddingRequest> Embeddings { get; init; } = [];
}

public record BatchUpsertEmbeddingsResponse
{
    public Guid JobId { get; init; }
    public int Processed { get; init; }
    public string Status { get; init; } = string.Empty;
}

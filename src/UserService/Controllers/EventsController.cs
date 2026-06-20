using Kendo.UserService.Data;
using Kendo.UserService.Domain.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kendo.UserService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<EventsController> _logger;

    public EventsController(AppDbContext db, ILogger<EventsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Lists events for the current user (paginated).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var query = _db.Events
            .Where(e => e.CreatedByUserId == userId.Value)
            .OrderByDescending(e => e.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize });
    }

    /// <summary>
    /// Gets a single event by id (owner only).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var evt = await _db.Events
            .Include(e => e.Validation)
            .FirstOrDefaultAsync(e => e.Id == id && e.CreatedByUserId == userId.Value, ct);

        if (evt is null)
            return NotFound();

        return Ok(evt);
    }

    /// <summary>
    /// Creates a new event. Called by Gateway after W1 returns structured JSON.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEventRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var evt = new Event
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = userId.Value,
            Title = request.Title,
            Description = request.Description,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Location = request.Location,
            Headcount = request.Headcount,
            DietaryNotes = request.DietaryNotes,
            SourceText = request.SourceText,
            IngestionStatus = EventIngestionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Events.Add(evt);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = evt.Id }, evt);
    }

    /// <summary>
    /// Updates an event (owner only).
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var evt = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == id && e.CreatedByUserId == userId.Value, ct);

        if (evt is null)
            return NotFound();

        if (request.Title is not null) evt.Title = request.Title;
        if (request.Description is not null) evt.Description = request.Description;
        if (request.StartsAt is not null) evt.StartsAt = request.StartsAt.Value;
        if (request.EndsAt is not null) evt.EndsAt = request.EndsAt.Value;
        if (request.Location is not null) evt.Location = request.Location;
        if (request.Headcount is not null) evt.Headcount = request.Headcount;
        if (request.DietaryNotes is not null) evt.DietaryNotes = request.DietaryNotes;
        if (request.IngestionStatus is not null) evt.IngestionStatus = request.IngestionStatus.Value;

        evt.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(evt);
    }

    /// <summary>
    /// Soft-deletes an event (owner only).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var evt = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == id && e.CreatedByUserId == userId.Value, ct);

        if (evt is null)
            return NotFound();

        _db.Events.Remove(evt);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────

public record CreateEventRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public string? Location { get; init; }
    public int? Headcount { get; init; }
    public string? DietaryNotes { get; init; }
    public string SourceText { get; init; } = string.Empty;
}

public record UpdateEventRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }
    public string? Location { get; init; }
    public int? Headcount { get; init; }
    public string? DietaryNotes { get; init; }
    public EventIngestionStatus? IngestionStatus { get; init; }
}

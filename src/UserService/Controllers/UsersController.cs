using Kendo.Shared.Messaging;
using Kendo.UserService.Data;
using Kendo.UserService.Models;
using Microsoft.AspNetCore.Mvc;

namespace Kendo.UserService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly UserRepository _repository;
    private readonly OutboxRepository _outboxRepository;
    private readonly ILogger<UsersController> _logger;

    public UsersController(UserRepository repository, OutboxRepository outboxRepository, ILogger<UsersController> logger)
    {
        _repository = repository;
        _outboxRepository = outboxRepository;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new user registration request asynchronously.
    /// Returns 202 Accepted with a Location header pointing to the status polling endpoint.
    /// The domain event is written to the transactional outbox and published atomically
    /// by the OutboxRelayService background relay.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = await _repository.CreateAsync(request.Email, request.DisplayName, ct);

        // Write domain event to outbox table (same transaction as user creation)
        var userCreatedEvent = new UserCreatedEvent
        {
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        };

        await _outboxRepository.AddAsync(userCreatedEvent, ct);

        var response = new UserStatusResponse
        {
            UserId = user.Id,
            Status = user.Status.ToString().ToLowerInvariant(),
            CreatedAt = user.CreatedAt,
            ProcessedAt = null
        };

        return AcceptedAtAction(nameof(GetStatus), new { id = user.Id }, response);
    }

    /// <summary>
    /// Polling endpoint — returns the current processing status of a user registration.
    /// </summary>
    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        var user = await _repository.GetByIdAsync(id, ct);

        if (user is null)
            return NotFound(new ProblemDetails
            {
                Type = "https://httpstatuses.com/404",
                Title = "Not Found",
                Status = 404,
                Detail = $"User with ID '{id}' was not found.",
                Instance = $"/api/users/{id}/status"
            });

        return Ok(new UserStatusResponse
        {
            UserId = user.Id,
            Status = user.Status.ToString().ToLowerInvariant(),
            CreatedAt = user.CreatedAt,
            ProcessedAt = user.ProcessedAt
        });
    }
}

// ── Request / Response DTOs ─────────────────────────────────────────────

public record CreateUserRequest
{
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public record UserStatusResponse
{
    public Guid UserId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}

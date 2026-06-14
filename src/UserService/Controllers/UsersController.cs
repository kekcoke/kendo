using Kendo.Shared.Messaging;
using Kendo.UserService.Data;
using Kendo.UserService.Models;
using Microsoft.AspNetCore.Mvc;
using Rebus.Bus;

namespace Kendo.UserService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly UserRepository _repository;
    private readonly IBus _bus;
    private readonly ILogger<UsersController> _logger;

    public UsersController(UserRepository repository, IBus bus, ILogger<UsersController> logger)
    {
        _repository = repository;
        _bus = bus;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new user registration request asynchronously.
    /// Returns 202 Accepted with a Location header pointing to the status polling endpoint.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = await _repository.CreateAsync(request.Email, request.DisplayName, ct);

        // Publish domain event via Rebus (fire-and-forget; M2.3 will consume)
        if (_bus is not null)
        {
            try
            {
                await _bus.Send(new UserCreatedEvent
                {
                    UserId = user.Id,
                    Email = user.Email,
                    DisplayName = user.DisplayName
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish UserCreatedEvent for user {UserId}. Event will be retried or lost without outbox (M2.5 deferred).", user.Id);
            }
        }
        else
        {
            _logger.LogWarning("IBus is null (ASB not configured). UserCreatedEvent not published for user {UserId}.", user.Id);
        }

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

using Kendo.Shared.Resilience;

namespace Kendo.UserService.Data;

/// <summary>
/// Decorator that wraps AppDbContext operations with the Polly Retry + Circuit Breaker pipeline.
/// Any transient database failures are retried; repeated failures trip the circuit breaker.
/// </summary>
public class ResilientAppDbContext
{
    private readonly AppDbContext _inner;
    private readonly IResiliencePipeline _resilience;

    public ResilientAppDbContext(AppDbContext inner, IResiliencePipeline resilience)
    {
        _inner = inner;
        _resilience = resilience;
    }

    /// <summary>
    /// Execute a database operation through the resilience pipeline.
    /// Usage: await resilientDb.ExecuteAsync(ct => dbContext.SomeEntity.ToListAsync(ct));
    /// </summary>
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        return _resilience.ExecuteAsync(action, ct);
    }

    /// <summary>
    /// Execute a void database operation through the resilience pipeline.
    /// </summary>
    public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        return _resilience.ExecuteAsync(action, ct);
    }
}

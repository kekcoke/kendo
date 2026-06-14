using Kendo.Shared.Resilience;

namespace Kendo.Worker.Data;

/// <summary>
/// Decorator that wraps WorkerDbContext operations with the Polly Retry + Circuit Breaker pipeline.
/// Follows the same pattern as ResilientAppDbContext in UserService.
/// </summary>
public class WorkerResilientDbContext
{
    private readonly WorkerDbContext _inner;
    private readonly IResiliencePipeline _resilience;

    public WorkerResilientDbContext(WorkerDbContext inner, IResiliencePipeline resilience)
    {
        _inner = inner;
        _resilience = resilience;
    }

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        return _resilience.ExecuteAsync(action, ct);
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        return _resilience.ExecuteAsync(action, ct);
    }
}

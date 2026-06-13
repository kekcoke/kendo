using Kendo.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kendo.Tests.Worker;

[Trait("Category", "Unit")]
public class WorkerBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_GracefulShutdown_CompletesWithoutException()
    {
        var logger = NullLogger<WorkerBackgroundService>.Instance;
        var service = new WorkerBackgroundService(logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        // Should complete without throwing — graceful shutdown
        Assert.True(true);
    }

    [Fact]
    public void StartAsync_ReturnsCompletedTask()
    {
        var logger = NullLogger<WorkerBackgroundService>.Instance;
        var service = new WorkerBackgroundService(logger);

        var task = service.StartAsync(CancellationToken.None);

        Assert.True(task.IsCompletedSuccessfully);
    }
}

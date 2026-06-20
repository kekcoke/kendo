using Kendo.Shared.Authentication;

namespace Kendo.Gateway.Infrastructure.Auth;

/// <summary>
/// BackgroundService that periodically checks if the JWT signing key
/// has exceeded its rotation threshold and generates a new keypair.
/// On failure, retries on the next check interval.
/// </summary>
public class KeyRotationBackgroundService : BackgroundService
{
    private readonly RsaKeyProvider _keyProvider;
    private readonly ILogger<KeyRotationBackgroundService> _logger;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    public KeyRotationBackgroundService(
        RsaKeyProvider keyProvider,
        ILogger<KeyRotationBackgroundService> logger)
    {
        _keyProvider = keyProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KeyRotationBackgroundService started. " +
            "Check interval: {Interval}h. Last rotation: {LastRotation:F}.",
            CheckInterval.TotalHours, _keyProvider.LastRotation());

        // Run initial check shortly after startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_keyProvider.ShouldRotate())
                {
                    await RotateWithRetryAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error during key rotation check");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("KeyRotationBackgroundService stopped.");
    }

    private async Task RotateWithRetryAsync(CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Key rotation triggered. " +
                    "Attempt {Attempt}/{MaxRetries}.", attempt, MaxRetries);

                _keyProvider.RotateKey();

                _logger.LogInformation("Key rotation succeeded. " +
                    "New key ID: {KeyId}. Overlap window: 7 days.",
                    _keyProvider.CurrentKeyId());
                return;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex,
                    "Key rotation attempt {Attempt}/{MaxRetries} failed. " +
                    "Retrying in {Delay}s...",
                    attempt, MaxRetries, RetryDelay.TotalSeconds);

                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Key rotation failed after {MaxRetries} attempts. " +
                    "Will retry on next check interval.", MaxRetries);
                throw;
            }
        }
    }
}

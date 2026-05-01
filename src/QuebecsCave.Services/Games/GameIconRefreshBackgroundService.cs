using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuebecsCave.Services.Games;

/// <summary>
/// Drives <see cref="IGameIconRefresher"/> on a slow cadence (default daily).
/// First tick fires after a short startup delay so it doesn't race the seeder.
/// </summary>
public sealed class GameIconRefreshBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceProvider _services;
    private readonly ILogger<GameIconRefreshBackgroundService> _logger;

    public GameIconRefreshBackgroundService(
        IServiceProvider services,
        ILogger<GameIconRefreshBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var refresher = scope.ServiceProvider.GetRequiredService<IGameIconRefresher>();
                await refresher.RefreshAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Game-icon refresh sweep failed; will retry next interval.");
            }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}

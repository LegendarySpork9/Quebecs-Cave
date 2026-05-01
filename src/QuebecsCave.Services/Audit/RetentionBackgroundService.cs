using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Time;

namespace QuebecsCave.Services.Audit;

/// <summary>
/// Daily cleanup at the configured local hour (default 03:00). Deletes:
/// <list type="bullet">
///   <item>ApiCallLog, DownloaderEvent, WebsiteEvent, LoginAttempt, StreamView
///         older than RetentionDays (default 30).</item>
///   <item>ErrorLog older than RetentionDays AND status ∈ {Fixed, NonIssue}.
///         Open / Acknowledged / RaisedToGit errors are kept indefinitely.</item>
/// </list>
/// AuditHistory and Deletion are kept indefinitely.
/// </summary>
public sealed class RetentionBackgroundService : BackgroundService
{
    private const int BatchSize = 1000;

    private readonly IServiceProvider _services;
    private readonly IClock _clock;
    private readonly IOptionsMonitor<AuditOptions> _options;
    private readonly ILogger<RetentionBackgroundService> _logger;

    public RetentionBackgroundService(
        IServiceProvider services,
        IClock clock,
        IOptionsMonitor<AuditOptions> options,
        ILogger<RetentionBackgroundService> logger)
    {
        _services = services;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextRun();
                _logger.LogInformation("Retention sweep scheduled in {Delay} (next run at {RunAt}).",
                    delay, _clock.Now.Add(delay));
                await Task.Delay(delay, stoppingToken);

                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention sweep failed; will retry tomorrow.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        var cutoff = _clock.Now.AddDays(-Math.Max(1, opts.RetentionDays));
        _logger.LogInformation("Retention sweep starting; cutoff = {Cutoff}", cutoff);

        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var apiCalls = sp.GetRequiredService<IApiCallLogRepository>();
        var downloader = sp.GetRequiredService<IDownloaderEventRepository>();
        var website = sp.GetRequiredService<IWebsiteEventRepository>();
        var errors = sp.GetRequiredService<IErrorLogRepository>();
        var generic = sp.GetRequiredService<IRetentionRepository>();

        var summary = new
        {
            ApiCallLog       = await apiCalls.DeleteOlderThanAsync(cutoff, BatchSize, cancellationToken),
            DownloaderEvent  = await downloader.DeleteOlderThanAsync(cutoff, BatchSize, cancellationToken),
            WebsiteEvent     = await website.DeleteOlderThanAsync(cutoff, BatchSize, cancellationToken),
            ErrorLogResolved = await errors.DeleteResolvedOlderThanAsync(cutoff, BatchSize, cancellationToken),
            LoginAttempt     = await generic.DeleteLoginAttemptsOlderThanAsync(cutoff, BatchSize, cancellationToken),
            StreamView       = await generic.DeleteStreamViewsOlderThanAsync(cutoff, BatchSize, cancellationToken),
        };

        _logger.LogInformation("Retention sweep complete: {@Summary}", summary);
    }

    private TimeSpan TimeUntilNextRun()
    {
        var hour = Math.Clamp(_options.CurrentValue.RetentionRunHourLocal, 0, 23);
        var now = _clock.Now;
        var todayRun = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, now.Offset);
        var next = todayRun > now ? todayRun : todayRun.AddDays(1);
        return next - now;
    }
}

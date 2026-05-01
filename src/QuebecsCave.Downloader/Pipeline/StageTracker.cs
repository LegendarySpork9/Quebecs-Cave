using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Time;
using QuebecsCave.Downloader.Api;

namespace QuebecsCave.Downloader.Pipeline;

/// <summary>
/// Wraps a stage of the loop in timing + outcome tracking. Emits a
/// DownloaderEvent to the API regardless of success or failure, never
/// suppressing the original exception. The tracker is meant to be cheap to
/// instantiate per-stage; it does not batch on its own.
/// </summary>
internal sealed class StageTracker
{
    private readonly ICaveApiClient _api;
    private readonly IClock _clock;
    private readonly ILogger _logger;

    public StageTracker(ICaveApiClient api, IClock clock, ILogger logger)
    {
        _api = api;
        _clock = clock;
        _logger = logger;
    }

    public Task<T> RunAsync<T>(
        string stage,
        string? vodId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        string? payload = null)
    {
        return RunAsyncCore(stage, vodId, action, cancellationToken, payload);
    }

    public async Task RunAsync(
        string stage,
        string? vodId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        string? payload = null)
    {
        await RunAsyncCore(stage, vodId, async ct => { await action(ct); return 0; }, cancellationToken, payload);
    }

    private async Task<T> RunAsyncCore<T>(
        string stage,
        string? vodId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        string? payload)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action(cancellationToken);
            sw.Stop();
            _logger.LogInformation(
                "[{Stage}] {Vod} ok in {Ms}ms",
                stage, vodId ?? "-", sw.ElapsedMilliseconds);
            await PostEventAsync(stage, vodId, success: true, sw.ElapsedMilliseconds, payload, message: null, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[{Stage}] {Vod} failed after {Ms}ms",
                stage, vodId ?? "-", sw.ElapsedMilliseconds);
            await PostEventAsync(stage, vodId, success: false, sw.ElapsedMilliseconds, payload, message: ex.Message, cancellationToken);
            throw;
        }
    }

    private Task PostEventAsync(
        string stage, string? vodId, bool success, long durationMs,
        string? payload, string? message, CancellationToken cancellationToken)
    {
        var dto = new DownloaderEventDto(
            Stage: stage,
            TwitchVodId: vodId,
            Success: success,
            DurationMs: (int)durationMs,
            Payload: payload,
            Message: message,
            OccurredAt: _clock.Now);
        return _api.PostDownloaderEventsAsync(new[] { dto }, cancellationToken);
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Downloader.Api;
using QuebecsCave.Downloader.Storage;
using QuebecsCave.Downloader.YtDlp;

namespace QuebecsCave.Downloader.Pipeline;

public interface IDownloaderLoop
{
    /// <summary>Runs a single sweep and exits.</summary>
    Task RunOnceAsync(CancellationToken cancellationToken);

    /// <summary>Runs sweeps on the configured interval until cancellation.</summary>
    Task RunWatchAsync(CancellationToken cancellationToken);
}

public sealed class DownloaderLoop : IDownloaderLoop
{
    private readonly ITwitchClient _twitch;
    private readonly ICaveApiClient _api;
    private readonly IYtDlpRunner _yt;
    private readonly IFileStorage _storage;
    private readonly IClock _clock;
    private readonly DownloaderOptions _opts;
    private readonly TwitchBroadcasterOptions _twitchOpts;
    private readonly ILogger<DownloaderLoop> _logger;

    public DownloaderLoop(
        ITwitchClient twitch,
        ICaveApiClient api,
        IYtDlpRunner yt,
        IFileStorage storage,
        IClock clock,
        IOptions<DownloaderOptions> opts,
        IOptions<TwitchBroadcasterOptions> twitchOpts,
        ILogger<DownloaderLoop> logger)
    {
        _twitch = twitch;
        _api = api;
        _yt = yt;
        _storage = storage;
        _clock = clock;
        _opts = opts.Value;
        _twitchOpts = twitchOpts.Value;
        _logger = logger;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var tracker = new StageTracker(_api, _clock, _logger);

        try
        {
            await tracker.RunAsync(DownloaderStage.Heartbeat, null,
                _ => Task.CompletedTask, cancellationToken);

            // 1. Fetch the streamer's recent VODs.
            var vods = await tracker.RunAsync(DownloaderStage.TwitchVodList, null, async ct =>
            {
                if (string.IsNullOrEmpty(_twitchOpts.BroadcasterUserId))
                {
                    throw new InvalidOperationException("Twitch:BroadcasterUserId is not configured.");
                }
                return await _twitch.GetUserVodsAsync(
                    _twitchOpts.BroadcasterUserId, _opts.VodTake, "archive", ct);
            }, cancellationToken);

            // 2. Diff against what we already have.
            var knownIds = await _api.GetKnownVodIdsAsync(cancellationToken);
            var knownSet = new HashSet<string>(knownIds, StringComparer.OrdinalIgnoreCase);
            var newVods = vods.Where(v => !knownSet.Contains(v.Id)).ToList();

            _logger.LogInformation(
                "Helix returned {Total} VOD(s); {New} new, {Existing} already archived.",
                vods.Count, newVods.Count, vods.Count - newVods.Count);

            // 3. Process new VODs. Resolve the fallback game lazily — only
            //    bother when there's actually something to attribute, so empty
            //    sweeps don't make a redundant /api/games call.
            if (newVods.Count > 0)
            {
                var defaultGame = await ResolveDefaultGameAsync(cancellationToken);
                foreach (var vod in newVods)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        await ProcessVodAsync(vod, defaultGame, tracker, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to ingest VOD {VodId}; reporting and continuing.", vod.Id);
                        await ReportErrorAsync(ex, $"VOD {vod.Id}", cancellationToken);
                    }
                }
            }

            // 4. Retry thumbnails for any earlier streams that didn't have one
            //    when they were first ingested. Runs every sweep — including
            //    when there were no new VODs — so a thumbnail Twitch generates
            //    after we already ingested gets picked up without a second
            //    Helix call per stream.
            await RetryMissingThumbnailsAsync(vods, tracker, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Downloader sweep failed; reporting.");
            await ReportErrorAsync(ex, "RunOnce", cancellationToken);
        }
    }

    public async Task RunWatchAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(60, _opts.PollSeconds));
        _logger.LogInformation("Watch mode: polling every {Interval}.", interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            await RunOnceAsync(cancellationToken);
            try { await Task.Delay(interval, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessVodAsync(
        TwitchVod vod,
        GameDto? fallbackGame,
        StageTracker tracker,
        CancellationToken cancellationToken)
    {
        // Match the VOD to whatever game was being played at the time it
        // streamed. The API consults dbo.LiveSession; if it has no overlap
        // we fall back to the configured DefaultGameSlug.
        var publishedAt = vod.PublishedAt == default ? _clock.Now : vod.PublishedAt;
        var matchedGame = !string.IsNullOrEmpty(_twitchOpts.BroadcasterUserId)
            ? await _api.GetGameForTimeAsync(_twitchOpts.BroadcasterUserId, publishedAt, cancellationToken)
            : null;

        int gameId;
        string gameSlug;
        if (matchedGame is not null)
        {
            gameId = matchedGame.GameId;
            gameSlug = matchedGame.Slug;
            _logger.LogInformation("VOD {VodId} matched live session → game '{Slug}'.", vod.Id, gameSlug);
        }
        else if (fallbackGame is not null)
        {
            gameId = fallbackGame.Id;
            gameSlug = fallbackGame.Slug;
            _logger.LogInformation("VOD {VodId} no live-session match → fallback '{Slug}'.", vod.Id, gameSlug);
        }
        else
        {
            throw new InvalidOperationException(
                "No live-session match and no fallback game available. " +
                "Set Downloader:DefaultGameSlug to an existing game (e.g. 'variety').");
        }

        var year = publishedAt.Year;
        var videoPath = _storage.BuildVideoPath(gameSlug, year, vod.Id);
        var thumbPath = _storage.BuildThumbnailPath(gameSlug, year, vod.Id);

        // Download the video.
        var ytResult = await tracker.RunAsync(
            DownloaderStage.Download, vod.Id,
            ct => _yt.DownloadVideoAsync(vod.Id, videoPath, ct),
            cancellationToken,
            payload: $"\"{videoPath}\"");

        if (!ytResult.Success)
        {
            throw new InvalidOperationException(
                $"yt-dlp exited with code {ytResult.ExitCode}: " +
                (string.IsNullOrEmpty(ytResult.Stderr) ? "(no stderr)" : ytResult.Stderr));
        }

        // Save thumbnail eagerly. If Helix hasn't generated it yet, leave the
        // stream's thumbnail null on creation so the retry sweep fills it in
        // on a later pass.
        bool thumbnailReady = false;
        if (!string.IsNullOrEmpty(vod.ThumbnailUrlTemplate))
        {
            var url = vod.ThumbnailUrlTemplate.Replace("%{width}", "320").Replace("%{height}", "180");
            thumbnailReady = await tracker.RunAsync(DownloaderStage.Upload, vod.Id,
                ct => _storage.DownloadFileAsync(url, thumbPath, ct),
                cancellationToken);
        }

        // POST to the ingest API. ThumbnailUrl=null when we couldn't grab one
        // yet; the retry sweep below will patch it in a future pass.
        await tracker.RunAsync(DownloaderStage.Ingest, vod.Id, async ct =>
        {
            var request = new IngestStreamRequest(
                Title: string.IsNullOrEmpty(vod.Title) ? $"VOD {vod.Id}" : vod.Title,
                Description: string.IsNullOrEmpty(vod.Description) ? null : vod.Description,
                GameId: gameId,
                StreamedAt: publishedAt,
                DurationSeconds: vod.DurationSeconds,
                VideoUrl: _storage.ToPublicVideoUrl(gameSlug, year, vod.Id),
                ThumbnailUrl: thumbnailReady ? _storage.ToPublicThumbnailUrl(gameSlug, year, vod.Id) : null,
                TwitchVodId: vod.Id);
            await _api.CreateStreamAsync(request, ct);
        }, cancellationToken);
    }

    private async Task RetryMissingThumbnailsAsync(
        IReadOnlyList<TwitchVod> recentVods,
        StageTracker tracker,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StreamNeedingThumbnailDto> pending;
        try
        {
            pending = await _api.ListStreamsNeedingThumbnailsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't list streams-needing-thumbnails; skipping retry sweep.");
            return;
        }

        if (pending.Count == 0) return;

        var byVodId = recentVods.ToDictionary(v => v.Id, StringComparer.OrdinalIgnoreCase);
        _logger.LogInformation("Retrying {Count} pending thumbnail(s).", pending.Count);

        foreach (var item in pending)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!byVodId.TryGetValue(item.TwitchVodId, out var vod) || string.IsNullOrEmpty(vod.ThumbnailUrlTemplate))
            {
                continue;
            }

            // The API tells us the slug + year so we can write to the canonical
            // {root}/{slug}/{year}/{vod}.jpg path that matches where the video
            // for this stream lives. Fall back to "variety" if the game has
            // somehow been deleted out from under us — better than writing to
            // a path that won't match the video's location.
            var gameSlug = string.IsNullOrEmpty(item.GameSlug) ? "variety" : item.GameSlug;
            var year = item.StreamedAtYear > 0 ? item.StreamedAtYear : _clock.Now.Year;

            try
            {
                await tracker.RunAsync(DownloaderStage.Upload, item.TwitchVodId, async ct =>
                {
                    var url = vod.ThumbnailUrlTemplate.Replace("%{width}", "320").Replace("%{height}", "180");
                    var thumbPath = _storage.BuildThumbnailPath(gameSlug, year, item.TwitchVodId);
                    var ok = await _storage.DownloadFileAsync(url, thumbPath, ct);
                    if (!ok)
                    {
                        _logger.LogDebug("Thumbnail still not ready for {VodId}.", item.TwitchVodId);
                        return;
                    }

                    var publicUrl = _storage.ToPublicThumbnailUrl(gameSlug, year, item.TwitchVodId);
                    await _api.UpdateThumbnailAsync(item.StreamId, publicUrl, ct);
                    _logger.LogInformation(
                        "Patched thumbnail on stream {StreamId} ({VodId}) at {Url}.",
                        item.StreamId, item.TwitchVodId, publicUrl);
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail retry failed for stream {StreamId}.", item.StreamId);
            }
        }
    }

    private async Task<GameDto?> ResolveDefaultGameAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_opts.DefaultGameSlug))
        {
            var match = await _api.GetGameBySlugAsync(_opts.DefaultGameSlug, cancellationToken);
            if (match is not null) return match;
            _logger.LogInformation(
                "DefaultGameSlug '{Slug}' not found in /api/games — falling back to first available.",
                _opts.DefaultGameSlug);
        }
        var games = await _api.ListGamesAsync(cancellationToken);
        return games.FirstOrDefault();
    }

    private async Task ReportErrorAsync(Exception ex, string contextHint, CancellationToken cancellationToken)
    {
        try
        {
            await _api.PostErrorsAsync(new[]
            {
                new ErrorEventDto(
                    ExceptionType: ex.GetType().FullName ?? ex.GetType().Name,
                    Message: ex.Message.Length > 2000 ? ex.Message.Substring(0, 2000) : ex.Message,
                    StackTrace: ex.StackTrace,
                    Context: System.Text.Json.JsonSerializer.Serialize(new { context = contextHint }),
                    OccurredAt: _clock.Now),
            }, cancellationToken);
        }
        catch (Exception postEx)
        {
            _logger.LogWarning(postEx, "Failed to forward error to API; continuing.");
        }
    }
}

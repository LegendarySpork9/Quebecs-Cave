using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Core.Util;
using QuebecsCave.Services.Games;

namespace QuebecsCave.Services.Twitch;

/// <summary>
/// Periodically polls Twitch Helix /streams for the configured broadcaster
/// and does two things with the result:
///
/// 1. Pushes the snapshot into <see cref="LiveStatusService"/> for the live
///    pill in the UI.
/// 2. Records the session into dbo.LiveSession so the downloader can later
///    attribute a freshly-archived VOD to whatever game was being played at
///    the time. The session is closed when the channel goes offline.
///
/// In dev, <c>Twitch:DevForceLive=true</c> skips Helix and uses a fake
/// session so the pill can be exercised offline. The session-tracking writes
/// still happen against the fake state — handy for seeding demo data
/// without a real broadcast.
/// </summary>
public sealed class LiveStatusBackgroundService : BackgroundService
{
    private readonly LiveStatusService _state;
    private readonly IServiceProvider _services;
    private readonly IClock _clock;
    private readonly IHostEnvironment _env;
    private readonly TwitchOptions _twitchOptions;
    private readonly ILogger<LiveStatusBackgroundService> _logger;

    public LiveStatusBackgroundService(
        LiveStatusService state,
        IServiceProvider services,
        IClock clock,
        IHostEnvironment env,
        IOptions<TwitchOptions> twitchOptions,
        ILogger<LiveStatusBackgroundService> logger)
    {
        _state = state;
        _services = services;
        _clock = clock;
        _env = env;
        _twitchOptions = twitchOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(15, _twitchOptions.LiveStatusPollSeconds));
        try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live-status poll failed; will retry next interval.");
            }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (_env.IsDevelopment() && _twitchOptions.DevForceLive)
        {
            await HandleLiveAsync(
                gameId: "0",
                gameName: string.IsNullOrEmpty(_twitchOptions.DevForceLiveGame) ? "Variety" : _twitchOptions.DevForceLiveGame,
                title: string.IsNullOrEmpty(_twitchOptions.DevForceLiveTitle) ? "[DEV] Force-live" : _twitchOptions.DevForceLiveTitle,
                startedAt: _clock.Now.AddMinutes(-15),
                viewerCount: 42,
                cancellationToken);
            return;
        }

        if (string.IsNullOrEmpty(_twitchOptions.StreamerUserId))
        {
            _state.Update(LiveStatus.Offline(_clock.Now));
            return;
        }

        await using var scope = _services.CreateAsyncScope();
        var twitch = scope.ServiceProvider.GetRequiredService<ITwitchClient>();
        var stream = await twitch.GetLiveStreamAsync(_twitchOptions.StreamerUserId, cancellationToken);

        if (stream is null)
        {
            _state.Update(LiveStatus.Offline(_clock.Now));
            await CloseSessionAsync(scope.ServiceProvider, cancellationToken);
            return;
        }

        await HandleLiveAsync(
            gameId: string.IsNullOrEmpty(stream.GameId) ? null : stream.GameId,
            gameName: string.IsNullOrEmpty(stream.GameName) ? null : stream.GameName,
            title: string.IsNullOrEmpty(stream.Title) ? null : stream.Title,
            startedAt: stream.StartedAt == default ? _clock.Now : stream.StartedAt,
            viewerCount: stream.ViewerCount,
            cancellationToken);
    }

    private async Task HandleLiveAsync(
        string? gameId, string? gameName, string? title,
        DateTimeOffset startedAt, int viewerCount,
        CancellationToken cancellationToken)
    {
        _state.Update(new LiveStatus(
            IsLive: true,
            GameName: gameName,
            Title: title,
            StartedAt: startedAt,
            ViewerCount: viewerCount,
            CheckedAt: _clock.Now));

        if (string.IsNullOrEmpty(_twitchOptions.StreamerUserId)) return;

        await using var scope = _services.CreateAsyncScope();
        var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var sessions = scope.ServiceProvider.GetRequiredService<ILiveSessionRepository>();

        // Resolve (or create) the Game row for this Twitch game id, so the
        // session has a stable foreign key the downloader can use.
        int? resolvedGameId = null;
        if (!string.IsNullOrEmpty(gameName))
        {
            var twitch = scope.ServiceProvider.GetRequiredService<ITwitchClient>();
            resolvedGameId = await ResolveGameAsync(games, twitch, gameId, gameName, cancellationToken);
        }

        try
        {
            await sessions.UpsertActiveAsync(
                _twitchOptions.StreamerUserId,
                gameId,
                gameName,
                title,
                resolvedGameId,
                _clock.Now,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert live session.");
        }
    }

    private async Task CloseSessionAsync(IServiceProvider scope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_twitchOptions.StreamerUserId)) return;
        try
        {
            var sessions = scope.GetRequiredService<ILiveSessionRepository>();
            await sessions.CloseLatestActiveAsync(_twitchOptions.StreamerUserId, _clock.Now, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close live session.");
        }
    }

    private static async Task<int?> ResolveGameAsync(
        IGameRepository games,
        ITwitchClient twitch,
        string? twitchGameId,
        string gameName,
        CancellationToken cancellationToken)
    {
        var slug = SlugGenerator.Slugify(gameName);
        if (string.IsNullOrEmpty(slug)) return null;

        var existing = await games.GetBySlugAsync(slug, cancellationToken);
        if (existing is not null) return existing.GameId;

        // First-touch: try to populate IconUrl up-front so the new game shows
        // box-art immediately rather than waiting for the daily refresh sweep.
        // The refresh sweep will keep it in sync from then on (and respects
        // IsCustomIcon=true if the streamer later overrides it).
        string? iconUrl = null;
        if (!string.IsNullOrEmpty(twitchGameId))
        {
            try
            {
                var fetched = await twitch.GetGamesByIdAsync(new[] { twitchGameId }, cancellationToken);
                var t = fetched.FirstOrDefault();
                if (t is not null)
                {
                    iconUrl = GameIconRefresher.RenderBoxArt(t.BoxArtUrlTemplate);
                }
            }
            catch
            {
                // Box-art failure is non-fatal; the daily refresh sweep will
                // backfill on the next pass.
            }
        }

        var created = new Game
        {
            Name = gameName,
            Slug = slug,
            IconUrl = iconUrl,
            TwitchGameId = string.IsNullOrEmpty(twitchGameId) ? null : twitchGameId,
            IsCustomIcon = false,
            CreatedAt = DateTimeOffset.Now,
        };
        return await games.CreateAsync(created, cancellationToken);
    }
}

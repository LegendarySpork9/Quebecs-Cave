using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Services.Twitch;

namespace QuebecsCave.Services.Identity;

/// <summary>
/// Periodically refreshes the moderator cache by calling Twitch Helix
/// /moderation/moderators with the streamer's stored token. Failures are
/// logged but never crash the host — the previous cache is kept on error.
/// </summary>
public sealed class ModeratorCacheRefreshService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IClock _clock;
    private readonly TwitchOptions _twitchOptions;
    private readonly ILogger<ModeratorCacheRefreshService> _logger;

    public ModeratorCacheRefreshService(
        IServiceProvider services,
        IClock clock,
        IOptions<TwitchOptions> twitchOptions,
        ILogger<ModeratorCacheRefreshService> logger)
    {
        _services = services;
        _clock = clock;
        _twitchOptions = twitchOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(60, _twitchOptions.ModRefreshSeconds));
        // First refresh: short delay so the app finishes warming up.
        var firstDelay = TimeSpan.FromSeconds(20);
        try { await Task.Delay(firstDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Moderator cache refresh failed; will retry next interval.");
            }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_twitchOptions.StreamerUserId))
        {
            _logger.LogDebug("Twitch:StreamerUserId not configured — skipping moderator refresh.");
            return;
        }

        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IUserRepository>();
        var tokens = sp.GetRequiredService<ITwitchTokenRepository>();
        var twitch = sp.GetRequiredService<ITwitchClient>();
        var modsRepo = sp.GetRequiredService<IModeratorCacheRepository>();

        var streamerUser = await users.GetByTwitchUserIdAsync(_twitchOptions.StreamerUserId, cancellationToken);
        if (streamerUser is null)
        {
            _logger.LogDebug("Streamer hasn't logged in yet — skipping moderator refresh.");
            return;
        }

        var token = await tokens.GetForUserAsync(streamerUser.UserId, cancellationToken);
        if (token is null)
        {
            _logger.LogDebug("No Twitch token stored for the streamer — skipping moderator refresh.");
            return;
        }

        var accessToken = token.AccessToken;
        if (token.ExpiresAt < _clock.Now.AddMinutes(2))
        {
            try
            {
                var refreshed = await twitch.RefreshUserTokenAsync(token.RefreshToken, cancellationToken);
                await tokens.UpsertAsync(
                    streamerUser.UserId,
                    refreshed.AccessToken,
                    refreshed.RefreshToken,
                    _clock.Now.AddSeconds(refreshed.ExpiresInSeconds),
                    string.Join(' ', refreshed.Scopes),
                    cancellationToken);
                accessToken = refreshed.AccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Streamer's refresh token is no longer valid — they need to sign in again.");
                return;
            }
        }

        IReadOnlyList<TwitchModerator> mods;
        try
        {
            mods = await twitch.GetModeratorsAsync(_twitchOptions.StreamerUserId, accessToken, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Helix /moderation/moderators failed.");
            return;
        }

        await modsRepo.ReplaceAllAsync(
            mods.Select(m => (m.UserId, m.UserLogin)).ToArray(),
            _clock.Now,
            cancellationToken);

        _logger.LogInformation("Refreshed moderator cache: {Count} mod(s).", mods.Count);
    }
}

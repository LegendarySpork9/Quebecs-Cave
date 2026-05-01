using Microsoft.Extensions.Logging;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Twitch;

namespace QuebecsCave.Services.Games;

/// <summary>
/// Periodically refetches game box-art from Twitch Helix. Honours the
/// <c>IsCustomIcon</c> flag on Game — when the streamer ticks "Custom icon
/// (don't refetch from Twitch)" the row is left alone, even if its
/// <c>TwitchGameId</c> is set. Games without a <c>TwitchGameId</c> are
/// skipped (nothing to look up against).
/// </summary>
public interface IGameIconRefresher
{
    Task<int> RefreshAsync(CancellationToken cancellationToken);
}

public sealed class GameIconRefresher : IGameIconRefresher
{
    private const int HelixGamesPerCall = 100;
    private const int BoxArtWidth = 285;
    private const int BoxArtHeight = 380;

    private readonly IGameRepository _games;
    private readonly ITwitchClient _twitch;
    private readonly ILogger<GameIconRefresher> _logger;

    public GameIconRefresher(
        IGameRepository games,
        ITwitchClient twitch,
        ILogger<GameIconRefresher> logger)
    {
        _games = games;
        _twitch = twitch;
        _logger = logger;
    }

    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        var all = await _games.ListAsync(cancellationToken);
        var candidates = all
            .Where(g => !g.IsCustomIcon && !string.IsNullOrEmpty(g.TwitchGameId))
            .ToArray();

        if (candidates.Length == 0)
        {
            return 0;
        }

        var byTwitchId = new Dictionary<string, TwitchGame>(StringComparer.Ordinal);
        foreach (var batch in Chunk(candidates.Select(g => g.TwitchGameId!).Distinct().ToArray(), HelixGamesPerCall))
        {
            try
            {
                var fetched = await _twitch.GetGamesByIdAsync(batch, cancellationToken);
                foreach (var t in fetched)
                {
                    byTwitchId[t.Id] = t;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Box-art batch fetch failed; skipping {Count} game(s) this run.", batch.Count);
            }
        }

        var updated = 0;
        foreach (var g in candidates)
        {
            if (!byTwitchId.TryGetValue(g.TwitchGameId!, out var t)) continue;
            var iconUrl = RenderBoxArt(t.BoxArtUrlTemplate);
            if (string.Equals(iconUrl, g.IconUrl, StringComparison.Ordinal)) continue;

            try
            {
                await _games.UpdateIconAsync(g.GameId, iconUrl, cancellationToken);
                updated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update IconUrl for Game {GameId} ({Slug}).", g.GameId, g.Slug);
            }
        }

        if (updated > 0)
        {
            _logger.LogInformation("Refreshed box-art for {Count} game(s).", updated);
        }
        return updated;
    }

    internal static string RenderBoxArt(string template) =>
        template
            .Replace("{width}", BoxArtWidth.ToString())
            .Replace("{height}", BoxArtHeight.ToString());

    private static IEnumerable<IReadOnlyList<string>> Chunk(string[] ids, int size)
    {
        for (var i = 0; i < ids.Length; i += size)
        {
            yield return ids.AsMemory(i, Math.Min(size, ids.Length - i)).ToArray();
        }
    }
}

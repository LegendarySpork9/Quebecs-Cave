using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Core.Util;
using QuebecsCave.Services.Identity;
using QuebecsCave.Services.Twitch;
using Stream = QuebecsCave.Core.Domain.Stream;

namespace QuebecsCave.Services.Seed;

public sealed class DevDataSeederOptions
{
    /// <summary>
    /// Absolute path to wwwroot/emojis/. Required so the seeder can list
    /// the PNG files. The Web project resolves this from
    /// <c>IWebHostEnvironment.WebRootPath</c>.
    /// </summary>
    public string EmojiFolderPath { get; set; } = "";

    /// <summary>
    /// How many VODs to seed if Helix returns enough.
    /// </summary>
    public int VodTake { get; set; } = 10;
}

/// <summary>
/// First-run seeder. If the Stream table is empty, populate emojis, games,
/// and streams from real Twitch data. Idempotent: bails quickly if there is
/// already content.
/// </summary>
public sealed class DevDataSeeder : IDevDataSeeder
{
    private static readonly string[] SeedGameNames = new[]
    {
        "Stardew Valley",
        "Project Zomboid",
        "Minecraft",
        "Vampire Survivors",
        "RimWorld",
    };

    private readonly IEmojiRepository _emojis;
    private readonly IGameRepository _games;
    private readonly IStreamRepository _streams;
    private readonly IUserRepository _users;
    private readonly IDeveloperRepository _developers;
    private readonly IModeratorCacheRepository _moderators;
    private readonly ILiveSessionRepository _liveSessions;
    private readonly ITwitchClient _twitch;
    private readonly IClock _clock;
    private readonly TwitchOptions _twitchOptions;
    private readonly DevDataSeederOptions _seedOptions;
    private readonly ILogger<DevDataSeeder> _logger;

    public DevDataSeeder(
        IEmojiRepository emojis,
        IGameRepository games,
        IStreamRepository streams,
        IUserRepository users,
        IDeveloperRepository developers,
        IModeratorCacheRepository moderators,
        ILiveSessionRepository liveSessions,
        ITwitchClient twitch,
        IClock clock,
        IOptions<TwitchOptions> twitchOptions,
        IOptions<DevDataSeederOptions> seedOptions,
        ILogger<DevDataSeeder> logger)
    {
        _emojis = emojis;
        _games = games;
        _streams = streams;
        _users = users;
        _developers = developers;
        _moderators = moderators;
        _liveSessions = liveSessions;
        _twitch = twitch;
        _clock = clock;
        _twitchOptions = twitchOptions.Value;
        _seedOptions = seedOptions.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // Emojis are independent of streams — seed them whenever empty.
        await SeedEmojisAsync(cancellationToken);

        // Test users + role-table rows are also seeded independently of streams
        // so /dev/login works even after restarts where streams already exist.
        await SeedTestUsersAndRolesAsync(cancellationToken);

        // The "Variety" catch-all game is always present so the downloader has
        // a safe place to put VODs that don't match any live session. Idempotent.
        var varietyGame = await EnsureVarietyGameAsync(cancellationToken);

        var existingStreams = await _streams.RecentAsync(1, cancellationToken);
        if (existingStreams.Count == 0)
        {
            _logger.LogInformation("Seeding dev data: games + streams …");
            var gamesById = await SeedGamesAsync(cancellationToken);
            await SeedStreamsAsync(gamesById, cancellationToken);
            _logger.LogInformation("Dev data seed complete.");
        }
        else
        {
            _logger.LogInformation("Streams already present — skipping game/stream seed.");
        }

        // Demo data for the live-session matching + thumbnail pending placeholder.
        // Idempotent: only adds if there's nothing similar in the DB yet.
        await SeedLiveSessionDemoAsync(varietyGame, cancellationToken);
        await ClearOneStreamThumbnailForDemoAsync(cancellationToken);
    }

    private async Task<Game> EnsureVarietyGameAsync(CancellationToken cancellationToken)
    {
        var existing = await _games.GetBySlugAsync("variety", cancellationToken);
        if (existing is not null) return existing;
        var game = new Game
        {
            Name = "Variety",
            Slug = "variety",
            IconUrl = "/emojis/quebCozy.png",
            TwitchGameId = null,
            IsCustomIcon = true,
            CreatedAt = _clock.Now,
        };
        var id = await _games.CreateAsync(game, cancellationToken);
        _logger.LogInformation("Seeded fallback game 'Variety' (id={Id}).", id);
        return new Game
        {
            GameId = id, Name = game.Name, Slug = game.Slug,
            IconUrl = game.IconUrl, TwitchGameId = game.TwitchGameId,
            IsCustomIcon = game.IsCustomIcon, CreatedAt = game.CreatedAt,
        };
    }

    private async Task SeedLiveSessionDemoAsync(Game varietyGame, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_twitchOptions.StreamerUserId)) return;
        var existing = await _liveSessions.ListRecentAsync(_twitchOptions.StreamerUserId, 1, cancellationToken);
        if (existing.Count > 0) return;

        // Pick two seeded streams, fabricate a LiveSession that overlaps each
        // one's StreamedAt window, and re-attribute the stream to the matched
        // game. That way the user can see "Stream X is now under Game Y because
        // a session matched."
        var recent = await _streams.RecentAsync(5, cancellationToken);
        if (recent.Count < 2) return;

        var allGames = (await _games.ListAsync(cancellationToken)).ToList();
        if (allGames.Count == 0) return;

        // Use two distinct (non-Variety) games for the demo if possible.
        var demoGames = allGames.Where(g => g.GameId != varietyGame.GameId).Take(2).ToList();
        if (demoGames.Count < 2) demoGames = allGames.Take(2).ToList();

        var sample = recent.Take(demoGames.Count).ToList();
        for (var i = 0; i < sample.Count; i++)
        {
            var stream = sample[i];
            var game = demoGames[i];
            var sessionStart = stream.StreamedAt.AddMinutes(-5);
            var sessionEnd = stream.StreamedAt.AddSeconds(stream.DurationSeconds + 300);

            // UpsertActiveAsync writes a session and bumps LastSeenAt; close it
            // explicitly to mimic a session that has finished.
            var sessionId = await _liveSessions.UpsertActiveAsync(
                _twitchOptions.StreamerUserId,
                game.TwitchGameId,
                game.Name,
                stream.Title,
                game.GameId,
                sessionStart,
                cancellationToken);

            // Bump the open session's LastSeenAt to the stream end (mocking
            // continuous polling), then close.
            await _liveSessions.UpsertActiveAsync(
                _twitchOptions.StreamerUserId,
                game.TwitchGameId,
                game.Name,
                stream.Title,
                game.GameId,
                sessionEnd.AddSeconds(-1),
                cancellationToken);
            await _liveSessions.CloseLatestActiveAsync(_twitchOptions.StreamerUserId, sessionEnd, cancellationToken);

            // Re-attribute the stream so the UI shows the demo immediately.
            if (stream.GameId != game.GameId)
            {
                await _streams.UpdateGameAsync(stream.StreamId, game.GameId, cancellationToken);
                _logger.LogInformation(
                    "Demo: re-attributed stream {StreamId} to game '{Game}' via fake live-session.",
                    stream.StreamId, game.Slug);
            }
            _ = sessionId;
        }
    }

    private async Task ClearOneStreamThumbnailForDemoAsync(CancellationToken cancellationToken)
    {
        // If there's already a thumbnailless stream the borked placeholder is
        // already visible — nothing to do.
        var pending = await _streams.ListStreamsNeedingThumbnailsAsync(1, cancellationToken);
        if (pending.Count > 0) return;

        var recent = await _streams.RecentAsync(3, cancellationToken);
        var target = recent.FirstOrDefault(s => !string.IsNullOrEmpty(s.ThumbnailUrl));
        if (target is null) return;

        var sql = "UPDATE dbo.Stream SET ThumbnailUrl = NULL WHERE StreamId = @id;";
        // We don't have a "set thumbnail to null" repo method, but UpdateAsync
        // accepts null. Round-trip the existing record with ThumbnailUrl=null.
        var blanked = new Stream
        {
            StreamId = target.StreamId,
            Title = target.Title,
            Description = target.Description,
            GameId = target.GameId,
            StreamedAt = target.StreamedAt,
            DurationSeconds = target.DurationSeconds,
            VideoUrl = target.VideoUrl,
            ThumbnailUrl = null,
            TwitchVodId = target.TwitchVodId,
            CreatedAt = target.CreatedAt,
        };
        await _streams.UpdateAsync(blanked, cancellationToken);
        _logger.LogInformation(
            "Demo: cleared thumbnail on stream {StreamId} so the borked placeholder is visible.",
            target.StreamId);
        _ = sql;
    }

    private async Task SeedTestUsersAndRolesAsync(CancellationToken cancellationToken)
    {
        var users = new Dictionary<string, User>(StringComparer.Ordinal);
        foreach (var profile in DevAuthProfiles.All)
        {
            users[profile.Key] = await _users.UpsertFromTwitchAsync(
                profile.TwitchUserId,
                profile.TwitchLogin,
                profile.DisplayName,
                profile.AvatarUrl,
                _clock.Now,
                cancellationToken);
        }

        // Make sure TestMod is in the moderator cache.
        var existingMods = await _moderators.ListAsync(cancellationToken);
        if (!existingMods.Any(m => m.TwitchUserId == DevAuthProfiles.TestModTwitchUserId))
        {
            // Preserve any real mods Helix has populated and add TestMod.
            var combined = existingMods
                .Select(m => (m.TwitchUserId, m.TwitchLogin))
                .Append((DevAuthProfiles.TestModTwitchUserId, "testmod"))
                .ToArray();
            await _moderators.ReplaceAllAsync(combined, _clock.Now, cancellationToken);
        }

        // Make sure TestDev is in the developer table.
        if (!await _developers.IsDeveloperAsync(DevAuthProfiles.TestDevTwitchUserId, cancellationToken))
        {
            var addedBy = users["streamer"].UserId;
            await _developers.AddAsync(
                DevAuthProfiles.TestDevTwitchUserId,
                "testdev",
                addedBy,
                _clock.Now,
                cancellationToken);
        }

        _logger.LogInformation("Test users + role rows seeded ({Count} users).", users.Count);
    }

    // ---- Emojis ----------------------------------------------------------

    private async Task SeedEmojisAsync(CancellationToken cancellationToken)
    {
        var existing = await _emojis.ListAllAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _logger.LogDebug("Emojis already seeded ({Count}).", existing.Count);
            return;
        }
        if (string.IsNullOrEmpty(_seedOptions.EmojiFolderPath) || !Directory.Exists(_seedOptions.EmojiFolderPath))
        {
            _logger.LogWarning("Emoji folder not configured or missing — skipping emoji seed.");
            return;
        }

        var files = Directory.EnumerateFiles(_seedOptions.EmojiFolderPath, "*.png").OrderBy(f => f).ToArray();
        var i = 0;
        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path);
            var code = Path.GetFileNameWithoutExtension(fileName);
            var name = PrettyEmojiName(code);
            var emoji = new Emoji
            {
                Code = code,
                Name = name,
                ImageUrl = "/emojis/" + fileName,
                IsActive = true,
                SortOrder = i++,
            };
            await _emojis.CreateAsync(emoji, cancellationToken);
        }
        _logger.LogInformation("Seeded {Count} emoji(s).", i);
    }

    // ---- Games -----------------------------------------------------------

    private async Task<Dictionary<string, Game>> SeedGamesAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, TwitchGame> twitchByName;
        try
        {
            var fetched = await _twitch.GetGamesByNameAsync(SeedGameNames, cancellationToken);
            twitchByName = fetched.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
            _logger.LogInformation("Resolved {Count}/{Total} game icons via Helix.",
                twitchByName.Count, SeedGameNames.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Helix /games failed — seeding games without icons.");
            twitchByName = new();
        }

        var bySlug = new Dictionary<string, Game>(StringComparer.Ordinal);
        foreach (var name in SeedGameNames)
        {
            var slug = SlugGenerator.Slugify(name);
            twitchByName.TryGetValue(name, out var t);
            var iconUrl = t is null ? null : RenderBoxArtUrl(t.BoxArtUrlTemplate, 285, 380);
            var game = new Game
            {
                Name = name,
                Slug = slug,
                IconUrl = iconUrl,
                TwitchGameId = t?.Id,
                IsCustomIcon = false,
                CreatedAt = _clock.Now,
            };
            var id = await _games.CreateAsync(game, cancellationToken);
            bySlug[slug] = new Game
            {
                GameId = id,
                Name = game.Name,
                Slug = game.Slug,
                IconUrl = game.IconUrl,
                TwitchGameId = game.TwitchGameId,
                IsCustomIcon = game.IsCustomIcon,
                CreatedAt = game.CreatedAt,
            };
        }
        return bySlug;
    }

    // ---- Streams ---------------------------------------------------------

    private async Task SeedStreamsAsync(Dictionary<string, Game> gamesBySlug, CancellationToken cancellationToken)
    {
        IReadOnlyList<TwitchVod> vods;
        try
        {
            vods = await _twitch.GetUserVodsAsync(
                _twitchOptions.StreamerUserId, _seedOptions.VodTake, "archive", cancellationToken);
            _logger.LogInformation("Fetched {Count} VOD(s) from Helix.", vods.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Helix /videos failed — falling back to fake stream entries.");
            vods = BuildFakeVods(_seedOptions.VodTake);
        }

        if (vods.Count == 0)
        {
            _logger.LogWarning("No VODs available — falling back to fake stream entries.");
            vods = BuildFakeVods(_seedOptions.VodTake);
        }

        var gameList = gamesBySlug.Values.ToArray();
        var rng = new Random(68000893);  // deterministic across runs

        foreach (var vod in vods)
        {
            var game = gameList[rng.Next(gameList.Length)];
            var streamedAt = vod.PublishedAt == default ? _clock.Now.AddDays(-rng.Next(1, 90)) : vod.PublishedAt;
            var thumbnail = string.IsNullOrEmpty(vod.ThumbnailUrlTemplate)
                ? null
                : RenderTwitchThumbnail(vod.ThumbnailUrlTemplate, 320, 180);

            var stream = new Stream
            {
                Title = string.IsNullOrEmpty(vod.Title) ? "Untitled stream" : vod.Title,
                Description = string.IsNullOrEmpty(vod.Description) ? null : vod.Description,
                GameId = game.GameId,
                StreamedAt = streamedAt,
                DurationSeconds = vod.DurationSeconds > 0 ? vod.DurationSeconds : 60 * (30 + rng.Next(180)),
                VideoUrl = string.IsNullOrEmpty(vod.Url)
                    ? $"https://www.twitch.tv/videos/{vod.Id}"
                    : vod.Url,
                ThumbnailUrl = thumbnail,
                TwitchVodId = string.IsNullOrEmpty(vod.Id) ? null : vod.Id,
                CreatedAt = _clock.Now,
            };
            await _streams.CreateAsync(stream, cancellationToken);
        }
        _logger.LogInformation("Seeded {Count} stream(s).", vods.Count);
    }

    // ---- Helpers ---------------------------------------------------------

    private static string PrettyEmojiName(string code)
    {
        // 'quebKing' → 'Queb King'
        if (code.StartsWith("queb", StringComparison.OrdinalIgnoreCase) && code.Length > 4)
        {
            return "Queb " + code.Substring(4, 1).ToUpperInvariant() + code.Substring(5);
        }
        return code;
    }

    private static string RenderBoxArtUrl(string template, int width, int height) =>
        template.Replace("{width}", width.ToString()).Replace("{height}", height.ToString());

    private static string RenderTwitchThumbnail(string template, int width, int height) =>
        template.Replace("%{width}", width.ToString()).Replace("%{height}", height.ToString());

    private static IReadOnlyList<TwitchVod> BuildFakeVods(int count)
    {
        var fakes = new List<TwitchVod>(count);
        for (var i = 0; i < count; i++)
        {
            fakes.Add(new TwitchVod(
                Id: $"fake-{i + 1}",
                UserId: "68000893",
                UserLogin: "longlivequebec",
                Title: $"Fake stream {i + 1}",
                Description: "Seeded fake VOD because Helix wasn't reachable.",
                CreatedAt: default,
                PublishedAt: default,
                Url: "",
                ThumbnailUrlTemplate: "",
                ViewCount: 0,
                Type: "archive",
                DurationSeconds: 0));
        }
        return fakes;
    }
}

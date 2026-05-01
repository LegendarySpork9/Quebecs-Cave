namespace QuebecsCave.Services.Identity;

public sealed class DevAuthOptions
{
    public const string SectionName = "DevAuth";

    public bool Enabled { get; set; }
}

/// <summary>
/// Profile of a seeded development user. The dev login page renders one
/// button per profile and, when clicked, signs the user in with the given
/// Twitch user ID — bypassing the real Twitch OAuth flow entirely.
/// </summary>
public sealed record DevAuthProfile(
    string Key,                  // 'streamer' | 'moderator' | 'developer' | 'viewer'
    string Label,
    string TwitchUserId,
    string TwitchLogin,
    string DisplayName,
    string? AvatarUrl,
    string Tagline);

public static class DevAuthProfiles
{
    public const string TestStreamerTwitchUserId = "68000893";   // matches the real streamer ID
    public const string TestModTwitchUserId       = "99000001";
    public const string TestDevTwitchUserId       = "99000002";
    public const string TestViewerTwitchUserId    = "99000003";

    public static IReadOnlyList<DevAuthProfile> All { get; } = new[]
    {
        new DevAuthProfile(
            "streamer",
            "Sign in as Streamer",
            TestStreamerTwitchUserId,
            "longlivequebec",
            "TestStreamer (LongLiveQuebec)",
            "/streamer-avatar.png",
            "Full admin — games, streams, emojis, audit, reports."),
        new DevAuthProfile(
            "moderator",
            "Sign in as Moderator",
            TestModTwitchUserId,
            "testmod",
            "TestMod",
            null,
            "Reports + audit access; no admin CRUD."),
        new DevAuthProfile(
            "developer",
            "Sign in as Developer",
            TestDevTwitchUserId,
            "testdev",
            "TestDev",
            null,
            "Audit history only."),
        new DevAuthProfile(
            "viewer",
            "Sign in as Viewer",
            TestViewerTwitchUserId,
            "testviewer",
            "TestViewer",
            null,
            "Standard logged-in user — can react to streams."),
    };

    public static DevAuthProfile? FindByKey(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}

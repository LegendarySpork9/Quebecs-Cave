namespace QuebecsCave.Core.Twitch;

public sealed record TwitchVod(
    string Id,
    string UserId,
    string UserLogin,
    string Title,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset PublishedAt,
    string Url,
    string ThumbnailUrlTemplate,    // contains %{width} / %{height} placeholders
    int ViewCount,
    string Type,                    // 'archive' | 'highlight' | 'upload'
    int DurationSeconds);

public sealed record TwitchGame(
    string Id,
    string Name,
    string BoxArtUrlTemplate);      // contains {width} / {height} placeholders

public sealed record TwitchLiveStream(
    string UserId,
    string UserLogin,
    string GameId,
    string GameName,
    string Title,
    int ViewerCount,
    DateTimeOffset StartedAt,
    string ThumbnailUrlTemplate);

public sealed record TwitchModerator(string UserId, string UserLogin, string UserName);

public sealed record TwitchUser(
    string Id,
    string Login,
    string DisplayName,
    string? Email,
    string ProfileImageUrl,
    string? Description);

public sealed record TwitchUserToken(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string TokenType,
    IReadOnlyList<string> Scopes);

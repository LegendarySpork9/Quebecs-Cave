namespace QuebecsCave.Core.Domain;

public sealed class Developer
{
    public int DeveloperId { get; init; }
    public required string TwitchUserId { get; init; }
    public required string TwitchLogin { get; init; }
    public int AddedByUserId { get; init; }
    public DateTimeOffset AddedAt { get; init; }
}

public sealed class ModeratorCacheEntry
{
    public int ModeratorCacheId { get; init; }
    public required string TwitchUserId { get; init; }
    public required string TwitchLogin { get; init; }
    public DateTimeOffset RefreshedAt { get; init; }
}

public sealed class TwitchToken
{
    public int TwitchTokenId { get; init; }
    public int UserId { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public required string Scopes { get; init; }
}

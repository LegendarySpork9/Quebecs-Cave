namespace QuebecsCave.Core.Domain;

public sealed class User
{
    public int UserId { get; init; }
    public required string TwitchUserId { get; init; }
    public required string TwitchLogin { get; init; }
    public required string DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public string? ThemePreference { get; init; }
    public string? TimeZoneId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
}

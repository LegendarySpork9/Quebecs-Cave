namespace QuebecsCave.Core.Domain;

public sealed class LiveSession
{
    public int LiveSessionId { get; init; }
    public required string BroadcasterUserId { get; init; }
    public string? TwitchGameId { get; init; }
    public string? GameName { get; init; }
    public string? Title { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public int? ResolvedGameId { get; init; }
}

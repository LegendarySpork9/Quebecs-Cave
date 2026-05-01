namespace QuebecsCave.Core.Domain;

public sealed class Stream
{
    public int StreamId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public int GameId { get; init; }
    public DateTimeOffset StreamedAt { get; init; }
    public int DurationSeconds { get; init; }
    public required string VideoUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? TwitchVodId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

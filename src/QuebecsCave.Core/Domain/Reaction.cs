namespace QuebecsCave.Core.Domain;

public sealed class Reaction
{
    public int ReactionId { get; init; }
    public int StreamId { get; init; }
    public int UserId { get; init; }
    public int EmojiId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

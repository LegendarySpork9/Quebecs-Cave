namespace QuebecsCave.Core.Domain;

public sealed class Emoji
{
    public int EmojiId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
}

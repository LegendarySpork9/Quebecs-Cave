namespace QuebecsCave.Core.Domain;

public sealed class Game
{
    public int GameId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public string? IconUrl { get; init; }
    public string? TwitchGameId { get; init; }
    public bool IsCustomIcon { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

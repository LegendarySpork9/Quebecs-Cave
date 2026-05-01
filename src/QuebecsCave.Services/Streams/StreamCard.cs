namespace QuebecsCave.Services.Streams;

/// <summary>
/// A flattened view of a Stream + its Game suitable for cards/lists/viewer
/// pages. The page picks fields it cares about; the service enriches the
/// raw Stream with the related Game's name + slug + icon.
/// </summary>
public sealed record StreamCard(
    int StreamId,
    string Title,
    string? Description,
    int GameId,
    string GameName,
    string GameSlug,
    string? GameIconUrl,
    DateTimeOffset StreamedAt,
    int DurationSeconds,
    string VideoUrl,
    string? ThumbnailUrl,
    string? TwitchVodId);

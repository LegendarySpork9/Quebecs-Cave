using QuebecsCave.Core.Domain;
using Stream = QuebecsCave.Core.Domain.Stream;

namespace QuebecsCave.Core.Repositories;

public sealed record StreamFilter(
    int? GameId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Search,
    int Skip,
    int Take);

public interface IStreamRepository
{
    Task<IReadOnlyList<Stream>> ListAsync(StreamFilter filter, CancellationToken cancellationToken);
    Task<int> CountAsync(StreamFilter filter, CancellationToken cancellationToken);
    Task<Stream?> GetByIdAsync(int streamId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Stream>> RecentAsync(int take, CancellationToken cancellationToken);
    Task<int> CreateAsync(Stream stream, CancellationToken cancellationToken);
    Task UpdateAsync(Stream stream, CancellationToken cancellationToken);
    Task DeleteAsync(int streamId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetKnownTwitchVodIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Streams that haven't got a thumbnail yet but DO have a TwitchVodId
    /// (so the downloader can poll Helix for the thumbnail later).
    /// </summary>
    Task<IReadOnlyList<Stream>> ListStreamsNeedingThumbnailsAsync(int take, CancellationToken cancellationToken);

    Task UpdateThumbnailAsync(int streamId, string thumbnailUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Used by the API/ingest path to back-fill the game on a freshly-created
    /// stream once a matching live session is found.
    /// </summary>
    Task UpdateGameAsync(int streamId, int gameId, CancellationToken cancellationToken);
}

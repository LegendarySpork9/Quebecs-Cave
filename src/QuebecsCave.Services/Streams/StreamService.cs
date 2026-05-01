using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using Stream = QuebecsCave.Core.Domain.Stream;

namespace QuebecsCave.Services.Streams;

public sealed record StreamPage(IReadOnlyList<StreamCard> Items, int Total, int Skip, int Take);

public interface IStreamService
{
    Task<IReadOnlyList<StreamCard>> GetRecentAsync(int take, CancellationToken cancellationToken);
    Task<StreamPage> SearchAsync(StreamFilter filter, CancellationToken cancellationToken);
    Task<StreamCard?> GetByIdAsync(int streamId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StreamCard>> RelatedAsync(int streamId, int gameId, int take, CancellationToken cancellationToken);
}

public sealed class StreamService : IStreamService
{
    private readonly IStreamRepository _streams;
    private readonly IGameRepository _games;

    public StreamService(IStreamRepository streams, IGameRepository games)
    {
        _streams = streams;
        _games = games;
    }

    public async Task<IReadOnlyList<StreamCard>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var streams = await _streams.RecentAsync(take, cancellationToken);
        return await EnrichAsync(streams, cancellationToken);
    }

    public async Task<StreamPage> SearchAsync(StreamFilter filter, CancellationToken cancellationToken)
    {
        var listTask = _streams.ListAsync(filter, cancellationToken);
        var countTask = _streams.CountAsync(filter, cancellationToken);
        await Task.WhenAll(listTask, countTask);

        var items = await EnrichAsync(listTask.Result, cancellationToken);
        return new StreamPage(items, countTask.Result, filter.Skip, filter.Take);
    }

    public async Task<StreamCard?> GetByIdAsync(int streamId, CancellationToken cancellationToken)
    {
        var s = await _streams.GetByIdAsync(streamId, cancellationToken);
        if (s is null) return null;
        var enriched = await EnrichAsync(new[] { s }, cancellationToken);
        return enriched[0];
    }

    public async Task<IReadOnlyList<StreamCard>> RelatedAsync(int streamId, int gameId, int take, CancellationToken cancellationToken)
    {
        var filter = new StreamFilter(GameId: gameId, From: null, To: null, Search: null, Skip: 0, Take: take + 1);
        var items = await _streams.ListAsync(filter, cancellationToken);
        var filtered = items.Where(s => s.StreamId != streamId).Take(take).ToArray();
        return await EnrichAsync(filtered, cancellationToken);
    }

    private async Task<IReadOnlyList<StreamCard>> EnrichAsync(IReadOnlyList<Stream> streams, CancellationToken cancellationToken)
    {
        if (streams.Count == 0) return Array.Empty<StreamCard>();
        var games = await _games.ListAsync(cancellationToken);
        var byId = games.ToDictionary(g => g.GameId);
        return streams.Select(s =>
        {
            byId.TryGetValue(s.GameId, out var g);
            return new StreamCard(
                s.StreamId,
                s.Title,
                s.Description,
                s.GameId,
                g?.Name ?? "Unknown game",
                g?.Slug ?? "",
                g?.IconUrl,
                s.StreamedAt,
                s.DurationSeconds,
                s.VideoUrl,
                s.ThumbnailUrl,
                s.TwitchVodId);
        }).ToArray();
    }
}

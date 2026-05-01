namespace QuebecsCave.Core.Reports;

public sealed record ReportsKpis(
    int TotalStreams,
    int TotalViews,
    int TotalReactions,
    int TotalEmojis,
    int TotalGames);

public sealed record TopStreamRow(int StreamId, string Title, int ViewCount, DateTimeOffset StreamedAt);

public sealed record TopGameRow(int GameId, string Name, string Slug, int StreamCount, int? ViewCount);

public sealed record TopEmojiRow(int EmojiId, string Code, string ImageUrl, int ReactionCount);

public sealed record DailyCountRow(DateTimeOffset Date, int Count);

public sealed record ReportsBundle(
    ReportsKpis Kpis,
    IReadOnlyList<TopStreamRow> TopStreams,
    IReadOnlyList<TopGameRow> TopGames,
    IReadOnlyList<TopEmojiRow> TopEmojis,
    IReadOnlyList<DailyCountRow> ViewsLast30Days);

public interface IReportsRepository
{
    Task<ReportsKpis> GetKpisAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TopStreamRow>> GetTopStreamsAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<TopGameRow>> GetTopGamesAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<TopEmojiRow>> GetTopEmojisAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyCountRow>> GetDailyViewsAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Reports;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class ReportsRepository : IReportsRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ReportsRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<ReportsKpis> GetKpisAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                (SELECT COUNT(*) FROM dbo.Stream)         AS TotalStreams,
                (SELECT COUNT(*) FROM dbo.StreamView)     AS TotalViews,
                (SELECT COUNT(*) FROM dbo.Reaction)       AS TotalReactions,
                (SELECT COUNT(*) FROM dbo.Emoji)          AS TotalEmojis,
                (SELECT COUNT(*) FROM dbo.Game)           AS TotalGames;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        return new ReportsKpis(
            r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4));
    }

    public async Task<IReadOnlyList<TopStreamRow>> GetTopStreamsAsync(int take, CancellationToken cancellationToken)
    {
        var sql = @$"
            SELECT TOP (@take)
                s.StreamId, s.Title, COUNT(v.StreamViewId) AS Views, s.StreamedAt
            FROM dbo.Stream s
            LEFT JOIN dbo.StreamView v ON v.StreamId = s.StreamId
            GROUP BY s.StreamId, s.Title, s.StreamedAt
            ORDER BY Views DESC, s.StreamedAt DESC;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@take", take);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<TopStreamRow>();
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new TopStreamRow(
                r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetFieldValue<DateTimeOffset>(3)));
        }
        return list;
    }

    public async Task<IReadOnlyList<TopGameRow>> GetTopGamesAsync(int take, CancellationToken cancellationToken)
    {
        var sql = @$"
            SELECT TOP (@take)
                g.GameId, g.Name, g.Slug,
                COUNT(DISTINCT s.StreamId) AS StreamCount,
                COUNT(v.StreamViewId)      AS ViewCount
            FROM dbo.Game g
            LEFT JOIN dbo.Stream s ON s.GameId = g.GameId
            LEFT JOIN dbo.StreamView v ON v.StreamId = s.StreamId
            GROUP BY g.GameId, g.Name, g.Slug
            ORDER BY StreamCount DESC, g.Name;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@take", take);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<TopGameRow>();
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new TopGameRow(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4)));
        }
        return list;
    }

    public async Task<IReadOnlyList<TopEmojiRow>> GetTopEmojisAsync(int take, CancellationToken cancellationToken)
    {
        var sql = @$"
            SELECT TOP (@take)
                e.EmojiId, e.Code, e.ImageUrl, COUNT(r.ReactionId) AS Reactions
            FROM dbo.Emoji e
            LEFT JOIN dbo.Reaction r ON r.EmojiId = e.EmojiId
            GROUP BY e.EmojiId, e.Code, e.ImageUrl
            ORDER BY Reactions DESC, e.Code;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@take", take);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<TopEmojiRow>();
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new TopEmojiRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        }
        return list;
    }

    public async Task<IReadOnlyList<DailyCountRow>> GetDailyViewsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT CAST(ViewedAt AS date) AS Day, COUNT(*) AS Cnt
            FROM dbo.StreamView
            WHERE ViewedAt >= @since
            GROUP BY CAST(ViewedAt AS date)
            ORDER BY Day;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@since", since);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<DailyCountRow>();
        while (await r.ReadAsync(cancellationToken))
        {
            var day = r.GetDateTime(0);
            list.Add(new DailyCountRow(new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day)), r.GetInt32(1)));
        }
        return list;
    }
}

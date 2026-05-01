using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;
using Stream = QuebecsCave.Core.Domain.Stream;

namespace QuebecsCave.Data.Repositories;

public sealed class StreamRepository : IStreamRepository
{
    private readonly ISqlConnectionFactory _factory;

    public StreamRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string SelectColumns = @"
        StreamId, Title, Description, GameId, StreamedAt, DurationSeconds,
        VideoUrl, ThumbnailUrl, TwitchVodId, CreatedAt";

    public async Task<IReadOnlyList<Stream>> ListAsync(StreamFilter filter, CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT {SelectColumns}
            FROM dbo.Stream
            WHERE 1 = 1
              AND (@gameId IS NULL OR GameId = @gameId)
              AND (@from IS NULL OR StreamedAt >= @from)
              AND (@to IS NULL OR StreamedAt < @to)
              AND (@search IS NULL OR Title LIKE @search)
            ORDER BY StreamedAt DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;";

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        AddFilterParameters(cmd, filter);
        cmd.Parameters.AddWithValue("@skip", filter.Skip);
        cmd.Parameters.AddWithValue("@take", filter.Take);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var streams = new List<Stream>();
        while (await r.ReadAsync(cancellationToken))
        {
            streams.Add(Map(r));
        }
        return streams;
    }

    public async Task<int> CountAsync(StreamFilter filter, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM dbo.Stream
            WHERE 1 = 1
              AND (@gameId IS NULL OR GameId = @gameId)
              AND (@from IS NULL OR StreamedAt >= @from)
              AND (@to IS NULL OR StreamedAt < @to)
              AND (@search IS NULL OR Title LIKE @search);";

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        AddFilterParameters(cmd, filter);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    public async Task<Stream?> GetByIdAsync(int streamId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {SelectColumns} FROM dbo.Stream WHERE StreamId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", streamId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<Stream>> RecentAsync(int take, CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT TOP (@take) {SelectColumns}
            FROM dbo.Stream
            ORDER BY StreamedAt DESC;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@take", take);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var streams = new List<Stream>();
        while (await r.ReadAsync(cancellationToken))
        {
            streams.Add(Map(r));
        }
        return streams;
    }

    public async Task<int> CreateAsync(Stream stream, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.Stream
                (Title, Description, GameId, StreamedAt, DurationSeconds,
                 VideoUrl, ThumbnailUrl, TwitchVodId, CreatedAt)
            OUTPUT INSERTED.StreamId
            VALUES
                (@title, @description, @gameId, @streamedAt, @duration,
                 @videoUrl, @thumbnailUrl, @twitchVodId, @createdAt);";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@title", stream.Title);
        cmd.Parameters.AddWithValue("@description", (object?)stream.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gameId", stream.GameId);
        cmd.Parameters.AddWithValue("@streamedAt", stream.StreamedAt);
        cmd.Parameters.AddWithValue("@duration", stream.DurationSeconds);
        cmd.Parameters.AddWithValue("@videoUrl", stream.VideoUrl);
        cmd.Parameters.AddWithValue("@thumbnailUrl", (object?)stream.ThumbnailUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@twitchVodId", (object?)stream.TwitchVodId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", stream.CreatedAt);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }

    public async Task UpdateAsync(Stream stream, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.Stream
            SET Title = @title,
                Description = @description,
                GameId = @gameId,
                StreamedAt = @streamedAt,
                DurationSeconds = @duration,
                VideoUrl = @videoUrl,
                ThumbnailUrl = @thumbnailUrl,
                TwitchVodId = @twitchVodId
            WHERE StreamId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", stream.StreamId);
        cmd.Parameters.AddWithValue("@title", stream.Title);
        cmd.Parameters.AddWithValue("@description", (object?)stream.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gameId", stream.GameId);
        cmd.Parameters.AddWithValue("@streamedAt", stream.StreamedAt);
        cmd.Parameters.AddWithValue("@duration", stream.DurationSeconds);
        cmd.Parameters.AddWithValue("@videoUrl", stream.VideoUrl);
        cmd.Parameters.AddWithValue("@thumbnailUrl", (object?)stream.ThumbnailUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@twitchVodId", (object?)stream.TwitchVodId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int streamId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM dbo.Stream WHERE StreamId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", streamId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetKnownTwitchVodIdsAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT TwitchVodId
            FROM dbo.Stream
            WHERE TwitchVodId IS NOT NULL;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var ids = new List<string>();
        while (await r.ReadAsync(cancellationToken))
        {
            ids.Add(r.GetString(0));
        }
        return ids;
    }

    public async Task<IReadOnlyList<Stream>> ListStreamsNeedingThumbnailsAsync(int take, CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT TOP (@take) {SelectColumns}
            FROM dbo.Stream
            WHERE ThumbnailUrl IS NULL
              AND TwitchVodId IS NOT NULL
            ORDER BY StreamedAt DESC;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@take", take);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<Stream>();
        while (await r.ReadAsync(cancellationToken)) list.Add(Map(r));
        return list;
    }

    public async Task UpdateThumbnailAsync(int streamId, string thumbnailUrl, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.Stream SET ThumbnailUrl = @url WHERE StreamId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", streamId);
        cmd.Parameters.AddWithValue("@url", thumbnailUrl);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateGameAsync(int streamId, int gameId, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.Stream SET GameId = @gameId WHERE StreamId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", streamId);
        cmd.Parameters.AddWithValue("@gameId", gameId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddFilterParameters(SqlCommand cmd, StreamFilter filter)
    {
        cmd.Parameters.AddWithValue("@gameId", (object?)filter.GameId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@from",   (object?)filter.From   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to",     (object?)filter.To     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@search",
            string.IsNullOrWhiteSpace(filter.Search)
                ? (object)DBNull.Value
                : "%" + filter.Search + "%");
    }

    private static Stream Map(DbDataReader r) => new()
    {
        StreamId = r.GetInt32("StreamId"),
        Title = r.GetString("Title"),
        Description = r.GetNullableString("Description"),
        GameId = r.GetInt32("GameId"),
        StreamedAt = r.GetDateTimeOffset("StreamedAt"),
        DurationSeconds = r.GetInt32("DurationSeconds"),
        VideoUrl = r.GetString("VideoUrl"),
        ThumbnailUrl = r.GetNullableString("ThumbnailUrl"),
        TwitchVodId = r.GetNullableString("TwitchVodId"),
        CreatedAt = r.GetDateTimeOffset("CreatedAt"),
    };
}

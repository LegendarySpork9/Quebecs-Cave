using System.Data;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class ReactionRepository : IReactionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ReactionRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<Reaction>> ListByStreamAsync(int streamId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT ReactionId, StreamId, UserId, EmojiId, CreatedAt
            FROM dbo.Reaction
            WHERE StreamId = @streamId
            ORDER BY CreatedAt DESC;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@streamId", streamId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<Reaction>();
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new Reaction
            {
                ReactionId = r.GetInt32("ReactionId"),
                StreamId = r.GetInt32("StreamId"),
                UserId = r.GetInt32("UserId"),
                EmojiId = r.GetInt32("EmojiId"),
                CreatedAt = r.GetDateTimeOffset("CreatedAt"),
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<int>> ListEmojiIdsByStreamForUserAsync(int streamId, int userId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT EmojiId
            FROM dbo.Reaction
            WHERE StreamId = @streamId AND UserId = @userId;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@streamId", streamId);
        cmd.Parameters.AddWithValue("@userId", userId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var ids = new List<int>();
        while (await r.ReadAsync(cancellationToken)) ids.Add(r.GetInt32(0));
        return ids;
    }

    public async Task AddAsync(int streamId, int userId, int emojiId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Idempotent: re-POSTing the same emoji is a no-op (no exception).
        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM dbo.Reaction WHERE StreamId = @s AND UserId = @u AND EmojiId = @e)
            BEGIN
                INSERT INTO dbo.Reaction (StreamId, UserId, EmojiId, CreatedAt)
                VALUES (@s, @u, @e, @at);
            END;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@s", streamId);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@e", emojiId);
        cmd.Parameters.AddWithValue("@at", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(int streamId, int userId, int emojiId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM dbo.Reaction WHERE StreamId = @s AND UserId = @u AND EmojiId = @e;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@s", streamId);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@e", emojiId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReactionCount>> GetCountsForStreamAsync(int streamId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT EmojiId, COUNT(*) AS Cnt
            FROM dbo.Reaction
            WHERE StreamId = @streamId
            GROUP BY EmojiId;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@streamId", streamId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<ReactionCount>();
        while (await r.ReadAsync(cancellationToken)) list.Add(new ReactionCount(r.GetInt32(0), r.GetInt32(1)));
        return list;
    }
}

public sealed class StreamViewRepository : IStreamViewRepository
{
    private readonly ISqlConnectionFactory _factory;
    public StreamViewRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<int> CreateAsync(int streamId, int? userId, byte[] ipHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.StreamView (StreamId, UserId, IpHash, ViewedAt)
            OUTPUT INSERTED.StreamViewId
            VALUES (@stream, @user, @ip, @at);";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@stream", streamId);
        cmd.Parameters.AddWithValue("@user", (object?)userId ?? DBNull.Value);
        cmd.Parameters.Add("@ip", System.Data.SqlDbType.VarBinary, 32).Value = ipHash;
        cmd.Parameters.AddWithValue("@at", now);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }
}

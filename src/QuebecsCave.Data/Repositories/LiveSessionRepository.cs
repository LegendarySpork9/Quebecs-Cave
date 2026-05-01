using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class LiveSessionRepository : ILiveSessionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public LiveSessionRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string Columns = @"
        LiveSessionId, BroadcasterUserId, TwitchGameId, GameName, Title,
        StartedAt, LastSeenAt, EndedAt, ResolvedGameId";

    public async Task<int> UpsertActiveAsync(
        string broadcasterUserId,
        string? twitchGameId,
        string? gameName,
        string? title,
        int? resolvedGameId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Open session = EndedAt IS NULL. We collapse rapid game-changes into
        // one session per game so a quick title flicker doesn't fragment the
        // record. If the game changes mid-stream we close the previous session
        // and open a new one — that way each Stream maps to one game cleanly.
        const string sql = @"
            DECLARE @existingId INT;
            SELECT TOP 1 @existingId = LiveSessionId
            FROM dbo.LiveSession WITH (UPDLOCK, HOLDLOCK)
            WHERE BroadcasterUserId = @broadcaster
              AND EndedAt IS NULL
              AND ((@twitchGameId IS NULL AND TwitchGameId IS NULL)
                OR (TwitchGameId = @twitchGameId))
            ORDER BY StartedAt DESC;

            IF @existingId IS NOT NULL
            BEGIN
                UPDATE dbo.LiveSession
                SET LastSeenAt = @now,
                    Title = COALESCE(@title, Title),
                    GameName = COALESCE(@gameName, GameName),
                    ResolvedGameId = COALESCE(@resolvedGameId, ResolvedGameId)
                WHERE LiveSessionId = @existingId;
                SELECT @existingId;
            END
            ELSE
            BEGIN
                -- New game (or first session): close any other open sessions
                -- for this broadcaster first, since they're stale.
                UPDATE dbo.LiveSession
                SET EndedAt = @now
                WHERE BroadcasterUserId = @broadcaster AND EndedAt IS NULL;

                INSERT INTO dbo.LiveSession
                    (BroadcasterUserId, TwitchGameId, GameName, Title,
                     StartedAt, LastSeenAt, EndedAt, ResolvedGameId)
                OUTPUT INSERTED.LiveSessionId
                VALUES (@broadcaster, @twitchGameId, @gameName, @title,
                        @now, @now, NULL, @resolvedGameId);
            END;";

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@broadcaster", broadcasterUserId);
        cmd.Parameters.AddWithValue("@twitchGameId", (object?)twitchGameId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gameName", (object?)gameName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@resolvedGameId", (object?)resolvedGameId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", now);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }

    public async Task<int?> CloseLatestActiveAsync(
        string broadcasterUserId,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            DECLARE @id INT;
            SELECT TOP 1 @id = LiveSessionId
            FROM dbo.LiveSession
            WHERE BroadcasterUserId = @broadcaster AND EndedAt IS NULL
            ORDER BY StartedAt DESC;

            IF @id IS NOT NULL
            BEGIN
                UPDATE dbo.LiveSession SET EndedAt = @endedAt WHERE LiveSessionId = @id;
                SELECT @id;
            END
            ELSE
                SELECT NULL;";

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@broadcaster", broadcasterUserId);
        cmd.Parameters.AddWithValue("@endedAt", endedAt);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (int)result;
    }

    public async Task<LiveSession?> FindForTimeAsync(
        string broadcasterUserId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        // Treat open sessions as ending at LastSeenAt + 5 minutes for matching
        // purposes — if the poller crashed mid-stream, the VOD's publishedAt
        // can still match the most recent observation.
        var sql = $@"
            SELECT TOP 1 {Columns}
            FROM dbo.LiveSession
            WHERE BroadcasterUserId = @broadcaster
              AND StartedAt <= @at
              AND (
                    (EndedAt IS NOT NULL AND @at < EndedAt)
                 OR (EndedAt IS NULL     AND @at < DATEADD(MINUTE, 5, LastSeenAt))
                  )
            ORDER BY StartedAt DESC;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@broadcaster", broadcasterUserId);
        cmd.Parameters.AddWithValue("@at", at);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<LiveSession>> ListRecentAsync(string broadcasterUserId, int take, CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT TOP (@take) {Columns}
            FROM dbo.LiveSession
            WHERE BroadcasterUserId = @broadcaster
            ORDER BY StartedAt DESC;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@broadcaster", broadcasterUserId);
        cmd.Parameters.AddWithValue("@take", take);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<LiveSession>();
        while (await r.ReadAsync(cancellationToken)) list.Add(Map(r));
        return list;
    }

    private static LiveSession Map(DbDataReader r) => new()
    {
        LiveSessionId = r.GetInt32("LiveSessionId"),
        BroadcasterUserId = r.GetString("BroadcasterUserId"),
        TwitchGameId = r.GetNullableString("TwitchGameId"),
        GameName = r.GetNullableString("GameName"),
        Title = r.GetNullableString("Title"),
        StartedAt = r.GetDateTimeOffset("StartedAt"),
        LastSeenAt = r.GetDateTimeOffset("LastSeenAt"),
        EndedAt = r.IsDBNull(r.GetOrdinal("EndedAt")) ? null : r.GetDateTimeOffset("EndedAt"),
        ResolvedGameId = r.GetNullableInt32("ResolvedGameId"),
    };
}

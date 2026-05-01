using System.Data;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly ISqlConnectionFactory _factory;

    public UserRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string Columns = @"
        UserId, TwitchUserId, TwitchLogin, DisplayName, AvatarUrl,
        ThemePreference, TimeZoneId, CreatedAt, LastSeenAt";

    public async Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Columns} FROM dbo.[User] WHERE UserId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", userId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<User?> GetByTwitchUserIdAsync(string twitchUserId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Columns} FROM dbo.[User] WHERE TwitchUserId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", twitchUserId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<User> UpsertFromTwitchAsync(
        string twitchUserId,
        string twitchLogin,
        string displayName,
        string? avatarUrl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            MERGE dbo.[User] AS target
            USING (SELECT @twitchUserId AS TwitchUserId) AS source
                ON target.TwitchUserId = source.TwitchUserId
            WHEN MATCHED THEN
                UPDATE SET TwitchLogin = @twitchLogin,
                           DisplayName = @displayName,
                           AvatarUrl = @avatarUrl,
                           LastSeenAt = @now
            WHEN NOT MATCHED THEN
                INSERT (TwitchUserId, TwitchLogin, DisplayName, AvatarUrl, CreatedAt, LastSeenAt)
                VALUES (@twitchUserId, @twitchLogin, @displayName, @avatarUrl, @now, @now)
            OUTPUT inserted.UserId, inserted.TwitchUserId, inserted.TwitchLogin,
                   inserted.DisplayName, inserted.AvatarUrl, inserted.ThemePreference,
                   inserted.TimeZoneId, inserted.CreatedAt, inserted.LastSeenAt;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@twitchUserId", twitchUserId);
        cmd.Parameters.AddWithValue("@twitchLogin", twitchLogin);
        cmd.Parameters.AddWithValue("@displayName", displayName);
        cmd.Parameters.AddWithValue("@avatarUrl", (object?)avatarUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", now);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("MERGE returned no row");
        }
        return Map(r);
    }

    public async Task UpdatePreferencesAsync(
        int userId,
        string? themePreference,
        string? timeZoneId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.[User]
            SET ThemePreference = @theme,
                TimeZoneId = @tz
            WHERE UserId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@theme", (object?)themePreference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tz", (object?)timeZoneId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static User Map(System.Data.Common.DbDataReader r) => new()
    {
        UserId = r.GetInt32("UserId"),
        TwitchUserId = r.GetString("TwitchUserId"),
        TwitchLogin = r.GetString("TwitchLogin"),
        DisplayName = r.GetString("DisplayName"),
        AvatarUrl = r.GetNullableString("AvatarUrl"),
        ThemePreference = r.GetNullableString("ThemePreference"),
        TimeZoneId = r.GetNullableString("TimeZoneId"),
        CreatedAt = r.GetDateTimeOffset("CreatedAt"),
        LastSeenAt = r.GetDateTimeOffset("LastSeenAt"),
    };
}

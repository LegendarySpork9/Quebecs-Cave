using System.Data;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class DeveloperRepository : IDeveloperRepository
{
    private readonly ISqlConnectionFactory _factory;
    public DeveloperRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string Columns = "DeveloperId, TwitchUserId, TwitchLogin, AddedByUserId, AddedAt";

    public async Task<IReadOnlyList<Developer>> ListAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM dbo.Developer ORDER BY TwitchLogin;";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<Developer>();
        while (await r.ReadAsync(cancellationToken)) list.Add(Map(r));
        return list;
    }

    public async Task<bool> IsDeveloperAsync(string twitchUserId, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM dbo.Developer WHERE TwitchUserId = @id;";
        cmd.Parameters.AddWithValue("@id", twitchUserId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    public async Task<int> AddAsync(string twitchUserId, string twitchLogin, int addedByUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.Developer (TwitchUserId, TwitchLogin, AddedByUserId, AddedAt)
            OUTPUT INSERTED.DeveloperId
            VALUES (@id, @login, @addedBy, @at);";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", twitchUserId);
        cmd.Parameters.AddWithValue("@login", twitchLogin);
        cmd.Parameters.AddWithValue("@addedBy", addedByUserId);
        cmd.Parameters.AddWithValue("@at", now);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }

    public async Task RemoveAsync(string twitchUserId, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.Developer WHERE TwitchUserId = @id;";
        cmd.Parameters.AddWithValue("@id", twitchUserId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Developer Map(System.Data.Common.DbDataReader r) => new()
    {
        DeveloperId = r.GetInt32("DeveloperId"),
        TwitchUserId = r.GetString("TwitchUserId"),
        TwitchLogin = r.GetString("TwitchLogin"),
        AddedByUserId = r.GetInt32("AddedByUserId"),
        AddedAt = r.GetDateTimeOffset("AddedAt"),
    };
}

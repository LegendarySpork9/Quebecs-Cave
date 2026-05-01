using System.Data;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class GameRepository : IGameRepository
{
    private readonly ISqlConnectionFactory _factory;

    public GameRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<Game>> ListAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT GameId, Name, Slug, IconUrl, TwitchGameId, IsCustomIcon, CreatedAt
            FROM dbo.Game
            ORDER BY Name;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var games = new List<Game>();
        while (await r.ReadAsync(cancellationToken))
        {
            games.Add(Map(r));
        }
        return games;
    }

    public async Task<Game?> GetByIdAsync(int gameId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT GameId, Name, Slug, IconUrl, TwitchGameId, IsCustomIcon, CreatedAt
            FROM dbo.Game
            WHERE GameId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", gameId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<Game?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT GameId, Name, Slug, IconUrl, TwitchGameId, IsCustomIcon, CreatedAt
            FROM dbo.Game
            WHERE Slug = @slug;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@slug", slug);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<int> CreateAsync(Game game, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.Game (Name, Slug, IconUrl, TwitchGameId, IsCustomIcon, CreatedAt)
            OUTPUT INSERTED.GameId
            VALUES (@name, @slug, @iconUrl, @twitchGameId, @isCustomIcon, @createdAt);";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@name", game.Name);
        cmd.Parameters.AddWithValue("@slug", game.Slug);
        cmd.Parameters.AddWithValue("@iconUrl", (object?)game.IconUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@twitchGameId", (object?)game.TwitchGameId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isCustomIcon", game.IsCustomIcon);
        cmd.Parameters.AddWithValue("@createdAt", game.CreatedAt);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }

    public async Task UpdateAsync(Game game, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.Game
            SET Name = @name,
                Slug = @slug,
                IconUrl = @iconUrl,
                TwitchGameId = @twitchGameId,
                IsCustomIcon = @isCustomIcon
            WHERE GameId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", game.GameId);
        cmd.Parameters.AddWithValue("@name", game.Name);
        cmd.Parameters.AddWithValue("@slug", game.Slug);
        cmd.Parameters.AddWithValue("@iconUrl", (object?)game.IconUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@twitchGameId", (object?)game.TwitchGameId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isCustomIcon", game.IsCustomIcon);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int gameId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM dbo.Game WHERE GameId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", gameId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateIconAsync(int gameId, string iconUrl, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.Game SET IconUrl = @iconUrl WHERE GameId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", gameId);
        cmd.Parameters.AddWithValue("@iconUrl", iconUrl);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Game Map(System.Data.Common.DbDataReader r) => new()
    {
        GameId = r.GetInt32("GameId"),
        Name = r.GetString("Name"),
        Slug = r.GetString("Slug"),
        IconUrl = r.GetNullableString("IconUrl"),
        TwitchGameId = r.GetNullableString("TwitchGameId"),
        IsCustomIcon = r.GetBoolean("IsCustomIcon"),
        CreatedAt = r.GetDateTimeOffset("CreatedAt"),
    };
}

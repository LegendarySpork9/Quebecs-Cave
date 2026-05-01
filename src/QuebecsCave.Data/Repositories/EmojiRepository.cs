using System.Data;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class EmojiRepository : IEmojiRepository
{
    private readonly ISqlConnectionFactory _factory;

    public EmojiRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string Columns = "EmojiId, Code, Name, ImageUrl, IsActive, SortOrder";

    public async Task<IReadOnlyList<Emoji>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT {Columns}
            FROM dbo.Emoji
            WHERE IsActive = 1
            ORDER BY SortOrder, Code;";
        return await ListAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<Emoji>> ListAllAsync(CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT {Columns}
            FROM dbo.Emoji
            ORDER BY SortOrder, Code;";
        return await ListAsync(sql, cancellationToken);
    }

    public async Task<Emoji?> GetByIdAsync(int emojiId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Columns} FROM dbo.Emoji WHERE EmojiId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", emojiId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<Emoji?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Columns} FROM dbo.Emoji WHERE Code = @code;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@code", code);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<int> CreateAsync(Emoji emoji, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.Emoji (Code, Name, ImageUrl, IsActive, SortOrder)
            OUTPUT INSERTED.EmojiId
            VALUES (@code, @name, @imageUrl, @isActive, @sortOrder);";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@code", emoji.Code);
        cmd.Parameters.AddWithValue("@name", emoji.Name);
        cmd.Parameters.AddWithValue("@imageUrl", emoji.ImageUrl);
        cmd.Parameters.AddWithValue("@isActive", emoji.IsActive);
        cmd.Parameters.AddWithValue("@sortOrder", emoji.SortOrder);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }

    public async Task UpdateAsync(Emoji emoji, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.Emoji
            SET Code = @code,
                Name = @name,
                ImageUrl = @imageUrl,
                IsActive = @isActive,
                SortOrder = @sortOrder
            WHERE EmojiId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", emoji.EmojiId);
        cmd.Parameters.AddWithValue("@code", emoji.Code);
        cmd.Parameters.AddWithValue("@name", emoji.Name);
        cmd.Parameters.AddWithValue("@imageUrl", emoji.ImageUrl);
        cmd.Parameters.AddWithValue("@isActive", emoji.IsActive);
        cmd.Parameters.AddWithValue("@sortOrder", emoji.SortOrder);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Emoji>> ListAsync(string sql, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<Emoji>();
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(Map(r));
        }
        return list;
    }

    private static Emoji Map(System.Data.Common.DbDataReader r) => new()
    {
        EmojiId = r.GetInt32("EmojiId"),
        Code = r.GetString("Code"),
        Name = r.GetString("Name"),
        ImageUrl = r.GetString("ImageUrl"),
        IsActive = r.GetBoolean("IsActive"),
        SortOrder = r.GetInt32("SortOrder"),
    };
}

using System.Data;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class ModeratorCacheRepository : IModeratorCacheRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ModeratorCacheRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string Columns = "ModeratorCacheId, TwitchUserId, TwitchLogin, RefreshedAt";

    public async Task<IReadOnlyList<ModeratorCacheEntry>> ListAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM dbo.ModeratorCache ORDER BY TwitchLogin;";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<ModeratorCacheEntry>();
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new ModeratorCacheEntry
            {
                ModeratorCacheId = r.GetInt32("ModeratorCacheId"),
                TwitchUserId = r.GetString("TwitchUserId"),
                TwitchLogin = r.GetString("TwitchLogin"),
                RefreshedAt = r.GetDateTimeOffset("RefreshedAt"),
            });
        }
        return list;
    }

    public async Task<bool> IsModeratorAsync(string twitchUserId, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM dbo.ModeratorCache WHERE TwitchUserId = @id;";
        cmd.Parameters.AddWithValue("@id", twitchUserId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    public async Task ReplaceAllAsync(
        IReadOnlyCollection<(string TwitchUserId, string TwitchLogin)> moderators,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var clear = (SqlCommand)conn.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM dbo.ModeratorCache;";
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var m in moderators)
            {
                await using var cmd = (SqlCommand)conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO dbo.ModeratorCache (TwitchUserId, TwitchLogin, RefreshedAt)
                    VALUES (@id, @login, @at);";
                cmd.Parameters.AddWithValue("@id", m.TwitchUserId);
                cmd.Parameters.AddWithValue("@login", m.TwitchLogin);
                cmd.Parameters.AddWithValue("@at", now);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

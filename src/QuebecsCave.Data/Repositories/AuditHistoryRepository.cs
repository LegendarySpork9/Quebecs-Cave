using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Audit;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class AuditHistoryRepository : IAuditHistoryRepository
{
    private readonly ISqlConnectionFactory _factory;
    public AuditHistoryRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<int> InsertAsync(int? userId, string entity, int entityId, string action, string? diff, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.AuditHistory (UserId, Entity, EntityId, Action, Diff, CreatedAt)
            OUTPUT INSERTED.AuditHistoryId
            VALUES (@user, @entity, @entityId, @action, @diff, @at);";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@user",     (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@entity",   entity);
        cmd.Parameters.AddWithValue("@entityId", entityId);
        cmd.Parameters.AddWithValue("@action",   action);
        cmd.Parameters.AddWithValue("@diff",     (object?)diff ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at",       now);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }
}

public sealed class DeletionRepository : IDeletionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public DeletionRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<int> InsertAsync(string entity, int entityId, int? userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.Deletion (Entity, EntityId, UserId, DeletedAt)
            OUTPUT INSERTED.DeletionId
            VALUES (@entity, @entityId, @user, @at);";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@entity",   entity);
        cmd.Parameters.AddWithValue("@entityId", entityId);
        cmd.Parameters.AddWithValue("@user",     (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at",       now);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }
}

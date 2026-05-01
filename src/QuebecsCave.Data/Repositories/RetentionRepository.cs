using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Audit;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class RetentionRepository : IRetentionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public RetentionRepository(ISqlConnectionFactory factory) => _factory = factory;

    public Task<int> DeleteLoginAttemptsOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken) =>
        DeleteLoopAsync(
            "DELETE TOP (@batch) FROM dbo.LoginAttempt WHERE AttemptedAt < @cutoff;",
            cutoff, batchSize, cancellationToken);

    public Task<int> DeleteStreamViewsOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken) =>
        DeleteLoopAsync(
            "DELETE TOP (@batch) FROM dbo.StreamView WHERE ViewedAt < @cutoff;",
            cutoff, batchSize, cancellationToken);

    private async Task<int> DeleteLoopAsync(string sql, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        await using var conn = await _factory.OpenAsync(cancellationToken);
        while (true)
        {
            await using var cmd = (SqlCommand)conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@batch", batchSize);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            total += rows;
            if (rows < batchSize) break;
        }
        return total;
    }
}

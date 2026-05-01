using System.Text;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Audit;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class ErrorLogRepository : IErrorLogRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ErrorLogRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task InsertManyAsync(IReadOnlyList<ErrorLogEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;

        var sb = new StringBuilder(@"INSERT INTO dbo.ErrorLog
            (Source, ExceptionType, Message, StackTrace, Context, StatusId,
             GitHubIssueUrl, AddressedByUserId, AddressedAt, Notes, OccurredAt)
            VALUES ");

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(@src{i},@et{i},@m{i},@st{i},@ctx{i},@stat{i},@gh{i},@ab{i},@aa{i},@n{i},@oc{i})");
            var e = entries[i];
            cmd.Parameters.AddWithValue($"@src{i}",  e.Source);
            cmd.Parameters.AddWithValue($"@et{i}",   e.ExceptionType);
            cmd.Parameters.AddWithValue($"@m{i}",    e.Message);
            cmd.Parameters.AddWithValue($"@st{i}",   (object?)e.StackTrace        ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@ctx{i}",  (object?)e.Context           ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@stat{i}", e.StatusId);
            cmd.Parameters.AddWithValue($"@gh{i}",   (object?)e.GitHubIssueUrl    ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@ab{i}",   (object?)e.AddressedByUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@aa{i}",   (object?)e.AddressedAt       ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@n{i}",    (object?)e.Notes             ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@oc{i}",   e.OccurredAt);
        }
        sb.Append(';');
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteResolvedOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        const string sql = @"
            DELETE TOP (@batch) FROM dbo.ErrorLog
            WHERE OccurredAt < @cutoff
              AND StatusId IN (
                SELECT ErrorStatusId FROM dbo.ErrorStatus WHERE Name IN ('Fixed', 'NonIssue')
              );";
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

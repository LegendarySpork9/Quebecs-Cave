using System.Text;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Audit;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class ApiCallLogRepository : IApiCallLogRepository
{
    private readonly ISqlConnectionFactory _factory;

    public ApiCallLogRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task InsertManyAsync(IReadOnlyList<ApiCallLogEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;

        var sb = new StringBuilder();
        sb.Append(@"INSERT INTO dbo.ApiCallLog
            (Method, Path, QueryString, RequestBody, ResponseStatus, ResponseBody,
             UserId, IpHash, ServiceKeyHash, DurationMs, CalledAt, RelatedAuditId)
            VALUES ");

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();

        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(@m{i},@p{i},@q{i},@rb{i},@rs{i},@rspb{i},@u{i},@ip{i},@sk{i},@d{i},@c{i},@ra{i})");
            var e = entries[i];
            cmd.Parameters.AddWithValue($"@m{i}", e.Method);
            cmd.Parameters.AddWithValue($"@p{i}", e.Path);
            cmd.Parameters.AddWithValue($"@q{i}",   (object?)e.QueryString   ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@rb{i}",  (object?)e.RequestBody   ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@rs{i}",  e.ResponseStatus);
            cmd.Parameters.AddWithValue($"@rspb{i}",(object?)e.ResponseBody  ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@u{i}",   (object?)e.UserId        ?? DBNull.Value);
            cmd.Parameters.Add($"@ip{i}",  System.Data.SqlDbType.VarBinary, 32).Value = e.IpHash;
            cmd.Parameters.Add($"@sk{i}",  System.Data.SqlDbType.VarBinary, 32).Value = (object?)e.ServiceKeyHash ?? DBNull.Value;
            cmd.Parameters.AddWithValue($"@d{i}",   e.DurationMs);
            cmd.Parameters.AddWithValue($"@c{i}",   e.CalledAt);
            cmd.Parameters.AddWithValue($"@ra{i}",  (object?)e.RelatedAuditId?? DBNull.Value);
        }
        sb.Append(';');
        cmd.CommandText = sb.ToString();

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        const string sql = "DELETE TOP (@batch) FROM dbo.ApiCallLog WHERE CalledAt < @cutoff;";
        return await DeleteLoopAsync(sql, cutoff, batchSize, cancellationToken);
    }

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

using System.Text;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Audit;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class WebsiteEventRepository : IWebsiteEventRepository
{
    private readonly ISqlConnectionFactory _factory;
    public WebsiteEventRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task InsertManyAsync(IReadOnlyList<WebsiteEventEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;

        var sb = new StringBuilder(@"INSERT INTO dbo.WebsiteEvent
            (Action, Path, UserId, IpHash, Detail, OccurredAt)
            VALUES ");

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(@a{i},@p{i},@u{i},@ip{i},@de{i},@oc{i})");
            var e = entries[i];
            cmd.Parameters.AddWithValue($"@a{i}",  e.Action);
            cmd.Parameters.AddWithValue($"@p{i}",  (object?)e.Path   ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@u{i}",  (object?)e.UserId ?? DBNull.Value);
            cmd.Parameters.Add($"@ip{i}", System.Data.SqlDbType.VarBinary, 32).Value = e.IpHash;
            cmd.Parameters.AddWithValue($"@de{i}", (object?)e.Detail ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@oc{i}", e.OccurredAt);
        }
        sb.Append(';');
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        const string sql = "DELETE TOP (@batch) FROM dbo.WebsiteEvent WHERE OccurredAt < @cutoff;";
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

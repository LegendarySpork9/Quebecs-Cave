using System.Text;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Audit;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class DownloaderEventRepository : IDownloaderEventRepository
{
    private readonly ISqlConnectionFactory _factory;
    public DownloaderEventRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task InsertManyAsync(IReadOnlyList<DownloaderEventEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;

        var sb = new StringBuilder(@"INSERT INTO dbo.DownloaderEvent
            (Stage, TwitchVodId, Success, DurationMs, Payload, Message, OccurredAt)
            VALUES ");

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(@st{i},@vod{i},@s{i},@d{i},@pl{i},@msg{i},@oc{i})");
            var e = entries[i];
            cmd.Parameters.AddWithValue($"@st{i}",  e.Stage);
            cmd.Parameters.AddWithValue($"@vod{i}", (object?)e.TwitchVodId ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@s{i}",   e.Success);
            cmd.Parameters.AddWithValue($"@d{i}",   (object?)e.DurationMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@pl{i}",  (object?)e.Payload    ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@msg{i}", (object?)e.Message    ?? DBNull.Value);
            cmd.Parameters.AddWithValue($"@oc{i}",  e.OccurredAt);
        }
        sb.Append(';');
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        const string sql = "DELETE TOP (@batch) FROM dbo.DownloaderEvent WHERE OccurredAt < @cutoff;";
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

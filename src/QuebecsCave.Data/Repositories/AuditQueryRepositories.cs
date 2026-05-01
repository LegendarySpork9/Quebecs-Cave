using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Audit;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class ApiCallLogQueryRepository : IApiCallLogQueryRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ApiCallLogQueryRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string SelectColumns = @"
        ApiCallLogId, Method, Path, QueryString, ResponseStatus,
        UserId, DurationMs, CalledAt, RelatedAuditId, RequestBody, ResponseBody";

    public async Task<AuditPage<ApiCallLogRow>> SearchAsync(ApiCallLogFilter filter, CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT {SelectColumns}
            FROM dbo.ApiCallLog
            WHERE 1 = 1
              AND (@from IS NULL OR CalledAt >= @from)
              AND (@to IS NULL OR CalledAt < @to)
              AND (@user IS NULL OR UserId = @user)
              AND (@path IS NULL OR Path LIKE @path)
              AND (@status IS NULL OR ResponseStatus = @status)
              AND (@method IS NULL OR Method = @method)
            ORDER BY CalledAt DESC, ApiCallLogId DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

            SELECT COUNT(*)
            FROM dbo.ApiCallLog
            WHERE 1 = 1
              AND (@from IS NULL OR CalledAt >= @from)
              AND (@to IS NULL OR CalledAt < @to)
              AND (@user IS NULL OR UserId = @user)
              AND (@path IS NULL OR Path LIKE @path)
              AND (@status IS NULL OR ResponseStatus = @status)
              AND (@method IS NULL OR Method = @method);";

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@from",   (object?)filter.From          ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to",     (object?)filter.To            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@user",   (object?)filter.UserId        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@path",   string.IsNullOrEmpty(filter.PathContains) ? (object)DBNull.Value : "%" + filter.PathContains + "%");
        cmd.Parameters.AddWithValue("@status", (object?)filter.StatusCode    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@method", string.IsNullOrEmpty(filter.Method) ? (object)DBNull.Value : filter.Method);
        cmd.Parameters.AddWithValue("@skip",   filter.Skip);
        cmd.Parameters.AddWithValue("@take",   filter.Take);

        var rows = new List<ApiCallLogRow>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) rows.Add(MapRow(r));
        await r.NextResultAsync(cancellationToken);
        var total = 0;
        if (await r.ReadAsync(cancellationToken)) total = r.GetInt32(0);
        return new AuditPage<ApiCallLogRow>(rows, total, filter.Skip, filter.Take);
    }

    public async Task<ApiCallLogRow?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {SelectColumns} FROM dbo.ApiCallLog WHERE ApiCallLogId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? MapRow(r) : null;
    }

    public async Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.ApiCallLog WHERE CalledAt >= @since;";
        cmd.Parameters.AddWithValue("@since", since);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static ApiCallLogRow MapRow(DbDataReader r) => new(
        r.GetInt64("ApiCallLogId"),
        r.GetString("Method"),
        r.GetString("Path"),
        r.GetNullableString("QueryString"),
        r.GetInt32("ResponseStatus"),
        r.GetNullableInt32("UserId"),
        r.GetInt32("DurationMs"),
        r.GetDateTimeOffset("CalledAt"),
        r.GetNullableInt32("RelatedAuditId"),
        r.GetNullableString("RequestBody"),
        r.GetNullableString("ResponseBody"));
}

public sealed class DownloaderEventQueryRepository : IDownloaderEventQueryRepository
{
    private readonly ISqlConnectionFactory _factory;
    public DownloaderEventQueryRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string SelectColumns = @"
        DownloaderEventId, Stage, TwitchVodId, Success, DurationMs, Message, Payload, OccurredAt";

    public async Task<AuditPage<DownloaderEventRow>> SearchAsync(DownloaderEventFilter filter, CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT {SelectColumns}
            FROM dbo.DownloaderEvent
            WHERE 1 = 1
              AND (@from IS NULL OR OccurredAt >= @from)
              AND (@to IS NULL OR OccurredAt < @to)
              AND (@stage IS NULL OR Stage = @stage)
              AND (@success IS NULL OR Success = @success)
              AND (@vod IS NULL OR TwitchVodId = @vod)
            ORDER BY OccurredAt DESC, DownloaderEventId DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

            SELECT COUNT(*) FROM dbo.DownloaderEvent
            WHERE 1 = 1
              AND (@from IS NULL OR OccurredAt >= @from)
              AND (@to IS NULL OR OccurredAt < @to)
              AND (@stage IS NULL OR Stage = @stage)
              AND (@success IS NULL OR Success = @success)
              AND (@vod IS NULL OR TwitchVodId = @vod);";

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@from",    (object?)filter.From    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to",      (object?)filter.To      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@stage",   string.IsNullOrEmpty(filter.Stage) ? (object)DBNull.Value : filter.Stage);
        cmd.Parameters.AddWithValue("@success", (object?)filter.Success ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vod",     string.IsNullOrEmpty(filter.TwitchVodId) ? (object)DBNull.Value : filter.TwitchVodId);
        cmd.Parameters.AddWithValue("@skip",    filter.Skip);
        cmd.Parameters.AddWithValue("@take",    filter.Take);

        var rows = new List<DownloaderEventRow>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new DownloaderEventRow(
                r.GetInt64("DownloaderEventId"),
                r.GetString("Stage"),
                r.GetNullableString("TwitchVodId"),
                r.GetBoolean("Success"),
                r.GetNullableInt32("DurationMs"),
                r.GetNullableString("Message"),
                r.GetNullableString("Payload"),
                r.GetDateTimeOffset("OccurredAt")));
        }
        await r.NextResultAsync(cancellationToken);
        var total = 0;
        if (await r.ReadAsync(cancellationToken)) total = r.GetInt32(0);
        return new AuditPage<DownloaderEventRow>(rows, total, filter.Skip, filter.Take);
    }

    public async Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.DownloaderEvent WHERE OccurredAt >= @since;";
        cmd.Parameters.AddWithValue("@since", since);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }
}

public sealed class WebsiteEventQueryRepository : IWebsiteEventQueryRepository
{
    private readonly ISqlConnectionFactory _factory;
    public WebsiteEventQueryRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<AuditPage<WebsiteEventRow>> SearchAsync(WebsiteEventFilter filter, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT WebsiteEventId, Action, Path, UserId, Detail, OccurredAt
            FROM dbo.WebsiteEvent
            WHERE 1 = 1
              AND (@from IS NULL OR OccurredAt >= @from)
              AND (@to IS NULL OR OccurredAt < @to)
              AND (@action IS NULL OR Action = @action)
              AND (@user IS NULL OR UserId = @user)
              AND (@path IS NULL OR Path LIKE @path)
            ORDER BY OccurredAt DESC, WebsiteEventId DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

            SELECT COUNT(*) FROM dbo.WebsiteEvent
            WHERE 1 = 1
              AND (@from IS NULL OR OccurredAt >= @from)
              AND (@to IS NULL OR OccurredAt < @to)
              AND (@action IS NULL OR Action = @action)
              AND (@user IS NULL OR UserId = @user)
              AND (@path IS NULL OR Path LIKE @path);";

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@from",   (object?)filter.From    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to",     (object?)filter.To      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", string.IsNullOrEmpty(filter.Action) ? (object)DBNull.Value : filter.Action);
        cmd.Parameters.AddWithValue("@user",   (object?)filter.UserId  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@path",   string.IsNullOrEmpty(filter.PathContains) ? (object)DBNull.Value : "%" + filter.PathContains + "%");
        cmd.Parameters.AddWithValue("@skip",   filter.Skip);
        cmd.Parameters.AddWithValue("@take",   filter.Take);

        var rows = new List<WebsiteEventRow>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new WebsiteEventRow(
                r.GetInt64("WebsiteEventId"),
                r.GetString("Action"),
                r.GetNullableString("Path"),
                r.GetNullableInt32("UserId"),
                r.GetNullableString("Detail"),
                r.GetDateTimeOffset("OccurredAt")));
        }
        await r.NextResultAsync(cancellationToken);
        var total = 0;
        if (await r.ReadAsync(cancellationToken)) total = r.GetInt32(0);
        return new AuditPage<WebsiteEventRow>(rows, total, filter.Skip, filter.Take);
    }

    public async Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.WebsiteEvent WHERE OccurredAt >= @since;";
        cmd.Parameters.AddWithValue("@since", since);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }
}

public sealed class ErrorLogQueryRepository : IErrorLogQueryRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ErrorLogQueryRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string SelectColumns = @"
        e.ErrorLogId, e.Source, e.ExceptionType, e.Message, e.StackTrace, e.Context,
        e.StatusId, s.Name AS StatusName,
        e.GitHubIssueUrl, e.AddressedByUserId, u.TwitchLogin AS AddressedByLogin,
        e.AddressedAt, e.Notes, e.OccurredAt";

    public async Task<AuditPage<ErrorLogRow>> SearchAsync(ErrorLogFilter filter, CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT {SelectColumns}
            FROM dbo.ErrorLog e
            INNER JOIN dbo.ErrorStatus s ON s.ErrorStatusId = e.StatusId
            LEFT JOIN dbo.[User] u ON u.UserId = e.AddressedByUserId
            WHERE 1 = 1
              AND (@from IS NULL OR e.OccurredAt >= @from)
              AND (@to IS NULL OR e.OccurredAt < @to)
              AND (@source IS NULL OR e.Source = @source)
              AND (@status IS NULL OR e.StatusId = @status)
              AND (@type IS NULL OR e.ExceptionType LIKE @type)
              AND (@search IS NULL OR e.Message LIKE @search OR e.StackTrace LIKE @search)
            ORDER BY e.OccurredAt DESC, e.ErrorLogId DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;

            SELECT COUNT(*)
            FROM dbo.ErrorLog e
            WHERE 1 = 1
              AND (@from IS NULL OR e.OccurredAt >= @from)
              AND (@to IS NULL OR e.OccurredAt < @to)
              AND (@source IS NULL OR e.Source = @source)
              AND (@status IS NULL OR e.StatusId = @status)
              AND (@type IS NULL OR e.ExceptionType LIKE @type)
              AND (@search IS NULL OR e.Message LIKE @search OR e.StackTrace LIKE @search);";

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@from",   (object?)filter.From    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to",     (object?)filter.To      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", string.IsNullOrEmpty(filter.Source) ? (object)DBNull.Value : filter.Source);
        cmd.Parameters.AddWithValue("@status", (object?)filter.StatusId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type",   string.IsNullOrEmpty(filter.ExceptionTypeContains) ? (object)DBNull.Value : "%" + filter.ExceptionTypeContains + "%");
        cmd.Parameters.AddWithValue("@search", string.IsNullOrEmpty(filter.Search) ? (object)DBNull.Value : "%" + filter.Search + "%");
        cmd.Parameters.AddWithValue("@skip",   filter.Skip);
        cmd.Parameters.AddWithValue("@take",   filter.Take);

        var rows = new List<ErrorLogRow>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) rows.Add(MapRow(r));
        await r.NextResultAsync(cancellationToken);
        var total = 0;
        if (await r.ReadAsync(cancellationToken)) total = r.GetInt32(0);
        return new AuditPage<ErrorLogRow>(rows, total, filter.Skip, filter.Take);
    }

    public async Task<ErrorLogRow?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT {SelectColumns}
            FROM dbo.ErrorLog e
            INNER JOIN dbo.ErrorStatus s ON s.ErrorStatusId = e.StatusId
            LEFT JOIN dbo.[User] u ON u.UserId = e.AddressedByUserId
            WHERE e.ErrorLogId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? MapRow(r) : null;
    }

    public async Task UpdateStatusAsync(
        long errorLogId,
        int statusId,
        string? gitHubIssueUrl,
        string? notes,
        int? addressedByUserId,
        DateTimeOffset? addressedAt,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.ErrorLog
            SET StatusId = @status,
                GitHubIssueUrl = COALESCE(@gh, GitHubIssueUrl),
                Notes = COALESCE(@notes, Notes),
                AddressedByUserId = @user,
                AddressedAt = @at
            WHERE ErrorLogId = @id;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id",     errorLogId);
        cmd.Parameters.AddWithValue("@status", statusId);
        cmd.Parameters.AddWithValue("@gh",     (object?)gitHubIssueUrl    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes",  (object?)notes             ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@user",   (object?)addressedByUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at",     (object?)addressedAt       ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM dbo.ErrorLog e
            INNER JOIN dbo.ErrorStatus s ON s.ErrorStatusId = e.StatusId
            WHERE s.Name IN ('Open', 'Acknowledged', 'RaisedToGit');";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static ErrorLogRow MapRow(DbDataReader r) => new(
        r.GetInt64("ErrorLogId"),
        r.GetString("Source"),
        r.GetString("ExceptionType"),
        r.GetString("Message"),
        r.GetNullableString("StackTrace"),
        r.GetNullableString("Context"),
        r.GetInt32("StatusId"),
        r.GetString("StatusName"),
        r.GetNullableString("GitHubIssueUrl"),
        r.GetNullableInt32("AddressedByUserId"),
        r.GetNullableString("AddressedByLogin"),
        r.IsDBNull(r.GetOrdinal("AddressedAt")) ? null : r.GetDateTimeOffset("AddressedAt"),
        r.GetNullableString("Notes"),
        r.GetDateTimeOffset("OccurredAt"));
}

public sealed class SchemaVersionQueryRepository : ISchemaVersionQueryRepository
{
    private readonly ISqlConnectionFactory _factory;
    public SchemaVersionQueryRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<SchemaVersionRow>> ListAsync(CancellationToken cancellationToken)
    {
        // DbUp's table uses [Id] / [ScriptName] / [Applied] (datetime). We
        // alias them so the audit page sees the *At naming convention.
        const string sql = @"
            SELECT [Id], [ScriptName], [Applied] AS AppliedAt
            FROM dbo.SchemaVersion
            ORDER BY [Id] DESC;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        var list = new List<SchemaVersionRow>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            // [Applied] is plain datetime — wrap as DateTimeOffset assuming local.
            var applied = r.GetFieldValue<DateTime>(r.GetOrdinal("AppliedAt"));
            list.Add(new SchemaVersionRow(
                r.GetInt32("Id"),
                r.GetString("ScriptName"),
                new DateTimeOffset(applied, TimeZoneInfo.Local.GetUtcOffset(applied))));
        }
        return list;
    }
}

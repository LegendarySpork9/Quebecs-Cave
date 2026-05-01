namespace QuebecsCave.Core.Audit;

public interface IApiCallLogQueryRepository
{
    Task<AuditPage<ApiCallLogRow>> SearchAsync(ApiCallLogFilter filter, CancellationToken cancellationToken);
    Task<ApiCallLogRow?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public interface IDownloaderEventQueryRepository
{
    Task<AuditPage<DownloaderEventRow>> SearchAsync(DownloaderEventFilter filter, CancellationToken cancellationToken);
    Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public interface IWebsiteEventQueryRepository
{
    Task<AuditPage<WebsiteEventRow>> SearchAsync(WebsiteEventFilter filter, CancellationToken cancellationToken);
    Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public interface IErrorLogQueryRepository
{
    Task<AuditPage<ErrorLogRow>> SearchAsync(ErrorLogFilter filter, CancellationToken cancellationToken);
    Task<ErrorLogRow?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task UpdateStatusAsync(
        long errorLogId,
        int statusId,
        string? gitHubIssueUrl,
        string? notes,
        int? addressedByUserId,
        DateTimeOffset? addressedAt,
        CancellationToken cancellationToken);
    Task<int> CountOpenAsync(CancellationToken cancellationToken);
}

public interface ISchemaVersionQueryRepository
{
    Task<IReadOnlyList<SchemaVersionRow>> ListAsync(CancellationToken cancellationToken);
}

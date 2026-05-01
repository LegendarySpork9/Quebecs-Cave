namespace QuebecsCave.Core.Audit;

public interface IApiCallLogRepository
{
    Task InsertManyAsync(IReadOnlyList<ApiCallLogEntry> entries, CancellationToken cancellationToken);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}

public interface IDownloaderEventRepository
{
    Task InsertManyAsync(IReadOnlyList<DownloaderEventEntry> entries, CancellationToken cancellationToken);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}

public interface IWebsiteEventRepository
{
    Task InsertManyAsync(IReadOnlyList<WebsiteEventEntry> entries, CancellationToken cancellationToken);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}

public interface IErrorLogRepository
{
    Task InsertManyAsync(IReadOnlyList<ErrorLogEntry> entries, CancellationToken cancellationToken);
    Task<int> DeleteResolvedOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}

public interface IRetentionRepository
{
    Task<int> DeleteLoginAttemptsOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
    Task<int> DeleteStreamViewsOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}

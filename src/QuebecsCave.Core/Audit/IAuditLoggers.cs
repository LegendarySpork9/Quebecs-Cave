namespace QuebecsCave.Core.Audit;

/// <summary>
/// Non-blocking audit writers. Implementations enqueue to an in-memory
/// channel; a BackgroundService flushes batches to SQL. Callers never
/// await the database — losing events is preferable to slowing requests.
/// </summary>
public interface IApiCallLogger
{
    void Enqueue(ApiCallLogEntry entry);
}

public interface IWebsiteEventLogger
{
    void Enqueue(WebsiteEventEntry entry);
}

public interface IDownloaderEventLogger
{
    void Enqueue(DownloaderEventEntry entry);
}

public interface IErrorLogger
{
    void Enqueue(ErrorLogEntry entry);
}

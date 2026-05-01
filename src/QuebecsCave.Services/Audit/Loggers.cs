using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuebecsCave.Core.Audit;

namespace QuebecsCave.Services.Audit;

// Each per-type logger is a small wrapper around a typed BatchedAuditWriter.
// The flusher subclasses delegate to the matching repository inside a fresh
// DI scope so scoped repos work safely from a singleton BackgroundService.

public sealed class ApiCallLogger : IApiCallLogger
{
    private readonly BatchedAuditWriter<ApiCallLogEntry> _writer;
    public ApiCallLogger(BatchedAuditWriter<ApiCallLogEntry> writer) => _writer = writer;
    public void Enqueue(ApiCallLogEntry entry) => _writer.Enqueue(entry);
}

public sealed class WebsiteEventLogger : IWebsiteEventLogger
{
    private readonly BatchedAuditWriter<WebsiteEventEntry> _writer;
    public WebsiteEventLogger(BatchedAuditWriter<WebsiteEventEntry> writer) => _writer = writer;
    public void Enqueue(WebsiteEventEntry entry) => _writer.Enqueue(entry);
}

public sealed class DownloaderEventLogger : IDownloaderEventLogger
{
    private readonly BatchedAuditWriter<DownloaderEventEntry> _writer;
    public DownloaderEventLogger(BatchedAuditWriter<DownloaderEventEntry> writer) => _writer = writer;
    public void Enqueue(DownloaderEventEntry entry) => _writer.Enqueue(entry);
}

public sealed class ErrorLogger : IErrorLogger
{
    private readonly BatchedAuditWriter<ErrorLogEntry> _writer;
    public ErrorLogger(BatchedAuditWriter<ErrorLogEntry> writer) => _writer = writer;
    public void Enqueue(ErrorLogEntry entry) => _writer.Enqueue(entry);
}

public sealed class ApiCallLogFlusher : BatchedAuditFlusher<ApiCallLogEntry>
{
    public ApiCallLogFlusher(IServiceProvider services, BatchedAuditWriter<ApiCallLogEntry> writer, ILogger<ApiCallLogFlusher> logger)
        : base(services, writer, logger) { }

    protected override Task FlushAsync(IServiceProvider scope, IReadOnlyList<ApiCallLogEntry> batch, CancellationToken cancellationToken)
    {
        var repo = scope.GetRequiredService<IApiCallLogRepository>();
        return repo.InsertManyAsync(batch, cancellationToken);
    }
}

public sealed class WebsiteEventFlusher : BatchedAuditFlusher<WebsiteEventEntry>
{
    public WebsiteEventFlusher(IServiceProvider services, BatchedAuditWriter<WebsiteEventEntry> writer, ILogger<WebsiteEventFlusher> logger)
        : base(services, writer, logger) { }

    protected override Task FlushAsync(IServiceProvider scope, IReadOnlyList<WebsiteEventEntry> batch, CancellationToken cancellationToken)
    {
        var repo = scope.GetRequiredService<IWebsiteEventRepository>();
        return repo.InsertManyAsync(batch, cancellationToken);
    }
}

public sealed class DownloaderEventFlusher : BatchedAuditFlusher<DownloaderEventEntry>
{
    public DownloaderEventFlusher(IServiceProvider services, BatchedAuditWriter<DownloaderEventEntry> writer, ILogger<DownloaderEventFlusher> logger)
        : base(services, writer, logger) { }

    protected override Task FlushAsync(IServiceProvider scope, IReadOnlyList<DownloaderEventEntry> batch, CancellationToken cancellationToken)
    {
        var repo = scope.GetRequiredService<IDownloaderEventRepository>();
        return repo.InsertManyAsync(batch, cancellationToken);
    }
}

public sealed class ErrorLogFlusher : BatchedAuditFlusher<ErrorLogEntry>
{
    public ErrorLogFlusher(IServiceProvider services, BatchedAuditWriter<ErrorLogEntry> writer, ILogger<ErrorLogFlusher> logger)
        : base(services, writer, logger) { }

    protected override Task FlushAsync(IServiceProvider scope, IReadOnlyList<ErrorLogEntry> batch, CancellationToken cancellationToken)
    {
        var repo = scope.GetRequiredService<IErrorLogRepository>();
        return repo.InsertManyAsync(batch, cancellationToken);
    }
}

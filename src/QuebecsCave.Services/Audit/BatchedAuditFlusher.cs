using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuebecsCave.Services.Audit;

/// <summary>
/// Reads audit items from <see cref="BatchedAuditWriter{T}"/> and flushes
/// them to SQL in batches. Subclasses implement the type-specific flush
/// using a fresh DI scope per batch (so scoped repos work).
///
/// On flush failure: log a warning and discard the batch. We don't retry —
/// retrying would risk an unbounded memory loop if SQL is permanently down.
/// New events keep flowing into the channel and will succeed once SQL recovers.
/// </summary>
public abstract class BatchedAuditFlusher<T> : BackgroundService where T : class
{
    private readonly IServiceProvider _services;
    private readonly BatchedAuditWriter<T> _writer;
    private readonly ILogger _logger;
    private readonly int _maxBatchSize;
    private readonly TimeSpan _flushInterval;

    protected BatchedAuditFlusher(
        IServiceProvider services,
        BatchedAuditWriter<T> writer,
        ILogger logger,
        int maxBatchSize = 100,
        TimeSpan? flushInterval = null)
    {
        _services = services;
        _writer = writer;
        _logger = logger;
        _maxBatchSize = maxBatchSize;
        _flushInterval = flushInterval ?? TimeSpan.FromMilliseconds(250);
    }

    protected abstract Task FlushAsync(IServiceProvider scope, IReadOnlyList<T> batch, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _writer.Reader;
        var batch = new List<T>(_maxBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Block until at least one item is available, or we shut down.
                var first = await reader.ReadAsync(stoppingToken);
                batch.Add(first);

                // Drain whatever is already queued, capped by batch size.
                while (batch.Count < _maxBatchSize && reader.TryRead(out var more))
                {
                    batch.Add(more);
                }

                // If there's headroom, give other producers a brief window to
                // arrive so we batch more efficiently.
                if (batch.Count < _maxBatchSize)
                {
                    using var delay = new CancellationTokenSource(_flushInterval);
                    while (batch.Count < _maxBatchSize && !delay.IsCancellationRequested)
                    {
                        try
                        {
                            var next = await reader.ReadAsync(delay.Token);
                            batch.Add(next);
                        }
                        catch (OperationCanceledException) when (delay.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                }

                await FlushBatchSafelyAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Drain whatever is left, then exit.
                while (reader.TryRead(out var leftover))
                {
                    batch.Add(leftover);
                }
                if (batch.Count > 0)
                {
                    await FlushBatchSafelyAsync(batch, CancellationToken.None);
                }
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit flusher loop caught an unexpected exception; continuing.");
            }
            finally
            {
                batch.Clear();
            }
        }
    }

    private async Task FlushBatchSafelyAsync(List<T> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;
        try
        {
            await using var scope = _services.CreateAsyncScope();
            await FlushAsync(scope.ServiceProvider, batch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audit flush dropped {Count} item(s) due to error; continuing.", batch.Count);
        }
    }
}

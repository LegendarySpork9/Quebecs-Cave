using System.Threading.Channels;

namespace QuebecsCave.Services.Audit;

/// <summary>
/// Bounded in-memory channel used to decouple producers (request pipeline,
/// blazor pages) from the SQL flush. Producer side is non-blocking; if the
/// channel is full, oldest items are dropped — losing audit events is
/// always preferable to slowing the request path.
/// </summary>
public sealed class BatchedAuditWriter<T>
{
    private readonly Channel<T> _channel;

    public BatchedAuditWriter(int capacity = 10_000)
    {
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool Enqueue(T item) => _channel.Writer.TryWrite(item);

    public ChannelReader<T> Reader => _channel.Reader;
}

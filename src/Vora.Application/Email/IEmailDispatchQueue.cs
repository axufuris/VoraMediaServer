using System.Threading.Channels;

namespace Vora.Application.Email;

public interface IEmailDispatchQueue
{
    ValueTask EnqueueAsync(QueuedEmail email, CancellationToken cancellationToken = default);
    ChannelReader<QueuedEmail> Reader { get; }
}

public class EmailDispatchQueue : IEmailDispatchQueue
{
    private const int QueueCapacity = 256;

    private readonly Channel<QueuedEmail> _channel = Channel.CreateBounded<QueuedEmail>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });

    public ChannelReader<QueuedEmail> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(QueuedEmail email, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(email, cancellationToken);
}

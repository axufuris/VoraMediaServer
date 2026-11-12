using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Vora.Application.Recommendations;

// One provider's For You rows for one profile, on Home (LibraryId null) or on a
// library's For You tab.
public readonly record struct ForYouRebuild(Guid ProfileId, Guid? LibraryId, string ProviderId);

// The rebuilds For You asks for while it shows the rows already stored.
// ForYouRebuildWorker runs them.
public interface IForYouRebuildQueue
{
    // Queues the rebuild unless the same one is already queued or running, or
    // failed or kept nothing within the cooldown. Never waits.
    bool TryQueue(ForYouRebuild rebuild);

    ChannelReader<ForYouRebuild> Reader { get; }

    // The worker is done with it. With coolDown it isn't queued again until the
    // cooldown has passed, so a provider that fails or finds nothing isn't run
    // again on every page view.
    void Finished(ForYouRebuild rebuild, bool coolDown);
}

public class ForYouRebuildQueue(TimeProvider time) : IForYouRebuildQueue
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    // More than enough: each profile, page and provider is in it at most once.
    // When it is full the rebuild is dropped, and the next page view asks again.
    private const int Capacity = 256;

    private readonly Channel<ForYouRebuild> _channel = Channel.CreateBounded<ForYouRebuild>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    private readonly ConcurrentDictionary<ForYouRebuild, byte> _pending = new();
    private readonly ConcurrentDictionary<ForYouRebuild, DateTime> _coolingUntil = new();

    public ChannelReader<ForYouRebuild> Reader => _channel.Reader;

    public bool TryQueue(ForYouRebuild rebuild)
    {
        if (_coolingUntil.TryGetValue(rebuild, out var until))
        {
            if (time.GetUtcNow().UtcDateTime < until) return false;
            _coolingUntil.TryRemove(KeyValuePair.Create(rebuild, until));
        }

        if (!_pending.TryAdd(rebuild, 0)) return false;
        if (_channel.Writer.TryWrite(rebuild)) return true;

        _pending.TryRemove(rebuild, out _);
        return false;
    }

    public void Finished(ForYouRebuild rebuild, bool coolDown)
    {
        // The cooldown is set before the rebuild stops being pending, so a page
        // view in between can't queue it again.
        if (coolDown) _coolingUntil[rebuild] = time.GetUtcNow().UtcDateTime + Cooldown;
        else _coolingUntil.TryRemove(rebuild, out _);

        _pending.TryRemove(rebuild, out _);
    }
}

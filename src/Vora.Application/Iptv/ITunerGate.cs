using System.Collections.Concurrent;

namespace Vora.Application.Iptv;

public interface ITunerGate
{
    Task<T> RunExclusiveAsync<T>(Guid playlistId, Func<Task<T>> action);
    Task RunExclusiveAsync(Guid playlistId, Func<Task> action);
}

public class TunerGate : ITunerGate
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<T> RunExclusiveAsync<T>(Guid playlistId, Func<Task<T>> action)
    {
        var gate = _locks.GetOrAdd(playlistId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RunExclusiveAsync(Guid playlistId, Func<Task> action)
    {
        await RunExclusiveAsync(playlistId, async () =>
        {
            await action();
            return true;
        });
    }
}

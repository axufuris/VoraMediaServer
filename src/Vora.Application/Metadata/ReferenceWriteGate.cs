namespace Vora.Application.Metadata;

// Serializes the creation of shared reference rows (actors, genres, companies,
// countries, networks, collections) across parallel enrichment workers. Each
// worker has its own DbContext, so without this two workers could both read
// "genre X missing", both insert it, and collide. The gate is a singleton so
// all scopes share it; the metadata FETCH (network) stays outside it and runs
// fully in parallel — only the brief shared-row read+insert+commit is serial.
public sealed class ReferenceWriteGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }
    }
}

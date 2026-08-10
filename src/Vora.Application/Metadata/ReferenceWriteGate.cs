namespace Vora.Application.Metadata;

// Serializes the creation of rows that parallel scan/enrichment workers can
// each try to create at the same time: shared reference rows (actors, genres,
// companies, countries, networks, collections) and shared media parents (a
// TV show or season referenced by many episode units). Each worker has its own
// DbContext, so without this two workers could both read "row missing", both
// insert it, and produce a duplicate. The gate is a singleton so all scopes
// share it; the metadata FETCH (network) stays outside it and runs fully in
// parallel — only the brief read+insert+commit is serial.
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

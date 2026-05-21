namespace Vora.Application.Logging;

public sealed class InMemoryLogBuffer : ILogBuffer
{
    private readonly LinkedList<LogEntry> _entries = new();
    private readonly object _lock = new();
    private readonly int _capacity;
    private long _nextId;

    public InMemoryLogBuffer(int capacity = 10_000)
    {
        if (capacity < 100) capacity = 100;
        _capacity = capacity;
    }

    public event Action<LogEntry>? EntryAppended;

    public long NextId() => Interlocked.Increment(ref _nextId);

    public void Append(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > _capacity)
            {
                _entries.RemoveFirst();
            }
        }

        var handler = EntryAppended;
        if (handler != null)
        {
            try
            {
                handler.Invoke(entry);
            }
            catch
            {
            }
        }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }
}

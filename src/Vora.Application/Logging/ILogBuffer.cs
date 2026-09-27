namespace Vora.Application.Logging;

public interface ILogBuffer
{
    void Append(LogEntry entry);
    IReadOnlyList<LogEntry> Snapshot();
    long NextId();
    event Action<LogEntry>? EntryAppended;
}

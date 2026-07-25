namespace Vora.Plugins.Interfaces;

public interface ITaskProgressReporter
{
    void Report(string? detail);
}

public sealed class NullTaskProgressReporter : ITaskProgressReporter
{
    public void Report(string? detail) { }
}

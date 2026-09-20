using Vora.Plugins.Interfaces;

namespace Vora.Application.Tasks;

public class TaskProgressReporter : ITaskProgressReporter
{
    private readonly ITaskQueueManager _queue;

    public TaskProgressReporter(ITaskQueueManager queue)
    {
        _queue = queue;
    }

    public void Report(string? detail) => _queue.ReportProgress(detail);
}

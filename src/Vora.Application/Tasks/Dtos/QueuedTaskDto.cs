namespace Vora.Application.Tasks.Dtos;

public class QueuedTaskDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public Func<CancellationToken, IServiceProvider, Task> WorkItem { get; set; } = null!;
}
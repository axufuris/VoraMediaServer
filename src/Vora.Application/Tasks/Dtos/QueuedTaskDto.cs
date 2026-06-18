namespace Vora.Application.Tasks.Dtos;

public class QueuedTaskDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public Func<CancellationToken, IServiceProvider, Task> WorkItem { get; set; } = null!;

    // Optional: resolves a friendly display name at run time (e.g. look up a
    // library/media title by id) so tasks don't show a raw GUID. Returns null
    // to keep the fallback Name.
    public Func<IServiceProvider, Task<string?>>? NameResolver { get; set; }
}
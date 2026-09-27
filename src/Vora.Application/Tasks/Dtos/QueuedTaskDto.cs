namespace Vora.Application.Tasks.Dtos;

public class QueuedTaskDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? Progress { get; set; }

    // Tasks that share a ResourceKey never run at the same time (e.g. every
    // heavy job on one library is keyed "library:{id}", so a scan and a refresh
    // of the same library serialize). Unkeyed tasks get a unique key, so they're
    // always eligible to run alongside others up to the global concurrency cap.
    public string ResourceKey { get; set; } = string.Empty;

    // Optional identity for the specific operation. When set, enqueuing is skipped
    // if a task with the same DedupeKey is already queued or running — e.g. the
    // daily thumbnail schedule firing while the same library's run is still going.
    // Distinct from ResourceKey, which only serializes *different* jobs on a shared
    // resource; two different ops share a ResourceKey but never a DedupeKey.
    public string? DedupeKey { get; set; }

    public Func<CancellationToken, IServiceProvider, Task> WorkItem { get; set; } = null!;

    // Optional: resolves a friendly display name at run time (e.g. look up a
    // library/media title by id) so tasks don't show a raw GUID. Returns null
    // to keep the fallback Name.
    public Func<IServiceProvider, Task<string?>>? NameResolver { get; set; }
}
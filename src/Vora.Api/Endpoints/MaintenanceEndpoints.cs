using Vora.Application.Maintenance;
using Vora.Application.Tasks;

namespace Vora.Api.Endpoints;

public static class MaintenanceEndpoints
{
    public static RouteGroupBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/maintenance")
            .WithTags("Admin", "Maintenance")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/unused-files", ScanUnusedFilesAsync)
            .WithName("ScanUnusedFiles")
            .Produces<UnusedFilesReportVM>(StatusCodes.Status200OK);
        group.MapPost("/unused-files/remove", RemoveUnusedFiles)
            .WithName("RemoveUnusedFiles")
            .Produces(StatusCodes.Status202Accepted);

        return group;
    }

    private static async Task<IResult> ScanUnusedFilesAsync(IUnusedFileManager manager, CancellationToken cancellationToken) =>
        Results.Ok(await manager.ScanAsync(cancellationToken));

    private static IResult RemoveUnusedFiles(ITaskQueueManager taskQueue)
    {
        taskQueue.QueueUnusedFileRemoval();
        return Results.Accepted();
    }
}

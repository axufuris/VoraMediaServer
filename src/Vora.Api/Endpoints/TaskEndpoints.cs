using Vora.Application.Tasks;
using Vora.Application.Tasks.ViewModels;

namespace Vora.Api.Endpoints;

public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/tasks").WithTags("Background Tasks").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAllTasks)
            .Produces<IEnumerable<QueuedTaskVM>>(StatusCodes.Status200OK);

        group.MapDelete("/{id:guid}", CancelTask)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult GetAllTasks(ITaskQueueManager queue) =>
        Results.Ok(queue.GetAllTasks());

    private static IResult CancelTask(Guid id, ITaskQueueManager queue)
    {
        var success = queue.CancelTask(id);
        return success ? Results.NoContent() : Results.NotFound();
    }
}

using Vora.Application.Tasks;
using Vora.Application.Tasks.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Vora.Api.Endpoints;

public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/tasks").WithTags("Background Tasks").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetTasks)
            .WithName("ListTasks")
            .Produces<QueuedTaskPageVM>(StatusCodes.Status200OK);

        group.MapDelete("/{id:guid}", CancelTask)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult GetTasks([FromQuery] int? skip, [FromQuery] int? take, ITaskQueueManager queue) =>
        Results.Ok(queue.GetTaskPage(skip ?? 0, take ?? DefaultTaskPageSize));

    private const int DefaultTaskPageSize = 25;

    private static IResult CancelTask(Guid id, ITaskQueueManager queue)
    {
        var success = queue.CancelTask(id);
        return success ? Results.NoContent() : Results.NotFound();
    }
}

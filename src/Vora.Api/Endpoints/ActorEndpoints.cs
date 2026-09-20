using Microsoft.AspNetCore.Mvc;
using Vora.Application.Actors;
using Vora.Application.Actors.Requests;
using Vora.Application.Actors.ViewModels;

namespace Vora.Api.Endpoints;

public static class ActorEndpoints
{
    public static RouteGroupBuilder MapActorEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/actors").WithTags("Actors").RequireAuthorization();

        group.MapGet("/{id:guid}", GetActorAsync)
            .Produces<ActorVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateActorAsync)
            .RequireAuthorization("AdminOnly")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> GetActorAsync(Guid id, IActorManager manager)
    {
        var actor = await manager.GetActorProfileAsync(id);
        return actor == null
            ? Results.NotFound(new { Message = "Actor not found." })
            : Results.Ok(actor);
    }

    private static async Task<IResult> CreateActorAsync([FromBody] CreateActorRequest request, IActorManager manager)
    {
        var newActorId = await manager.CreateCustomActorAsync(request.Name, request.ProfileImageUrl);
        return Results.Created($"/api/actors/{newActorId}", new { Id = newActorId });
    }
}

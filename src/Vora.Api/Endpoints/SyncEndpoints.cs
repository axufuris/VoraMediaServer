using Vora.Application.Sync;
using Vora.Application.Sync.ViewModels;

namespace Vora.Api.Endpoints;

public static class SyncEndpoints
{
    public static RouteGroupBuilder MapSyncEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/sync").WithTags("Sync & State").RequireAuthorization();

        group.MapGet("/profiles/{profileId:guid}/continue-watching", GetContinueWatchingAsync)
            .WithName("ListContinueWatching")
            .Produces<IEnumerable<ContinueWatchingVM>>(StatusCodes.Status200OK);

        group.MapPut("/profiles/{profileId:guid}/continue-watching/{mediaItemId:guid}/hide", HideFromContinueWatchingAsync)
            .WithName("HideFromContinueWatching");

        return group;
    }

    private static async Task<IResult> GetContinueWatchingAsync(Guid profileId, ISyncAndStateManager manager, int limit = 10)
    {
        var feed = await manager.GetContinueWatchingFeedAsync(profileId, limit);
        return Results.Ok(feed);
    }

    private static async Task<IResult> HideFromContinueWatchingAsync(Guid profileId, Guid mediaItemId, ISyncAndStateManager manager)
    {
        await manager.HideFromContinueWatchingAsync(profileId, mediaItemId);
        return Results.NoContent();
    }
}

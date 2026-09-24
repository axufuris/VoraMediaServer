using Vora.Application.Media.SmartPlaylists;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Playlists;
using Vora.Application.Playlists.ViewModels;

namespace Vora.Api.Endpoints;

public static class PlaylistEndpoints
{
    public static RouteGroupBuilder MapPlaylistEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/playlists").WithTags("Playlists").RequireAuthorization();

        group.MapGet("/", GetPlaylistsAsync)
            .WithName("ListPlaylists")
            .Produces<IEnumerable<PlaylistSummaryVM>>(StatusCodes.Status200OK);
        group.MapGet("/{id:guid}", GetPlaylistAsync)
            .WithName("GetPlaylistDetails")
            .Produces<PlaylistDetailsVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        // Everything other profiles have shared, manual and smart together.
        group.MapGet("/shared", GetSharedAsync)
            .WithName("ListSharedPlaylists")
            .Produces<SharedPlaylistsVM>(StatusCodes.Status200OK);
        group.MapGet("/contains/{mediaId:guid}", GetPlaylistsContainingAsync)
            .WithName("ListPlaylistsContainingMedia")
            .Produces<IEnumerable<Guid>>(StatusCodes.Status200OK);

        group.MapPost("/", CreatePlaylistAsync)
            .WithName("CreatePlaylist")
            .Produces<CreatePlaylistResponse>(StatusCodes.Status200OK);
        group.MapPost("/{id:guid}/items/{mediaId:guid}", AddItemAsync)
            .WithName("AddPlaylistItem")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/unwatch-all", MarkAllUnplayedAsync);

        // Owner only. 404 for anyone else, the same answer as a playlist that
        // does not exist, so the endpoint does not confirm what is there.
        group.MapPut("/{id:guid}/sharing", SetSharingAsync)
            .WithName("SetPlaylistSharing")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        // Your own, or anyone's shared one. The copy is yours and starts unshared.
        group.MapPost("/{id:guid}/copy", CopyAsync)
            .WithName("CopyPlaylist")
            .Produces<CreatePlaylistResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdatePlaylistAsync);
        group.MapPut("/{id:guid}/reorder", ReorderAsync);

        group.MapDelete("/{id:guid}", DeletePlaylistAsync);
        group.MapDelete("/{id:guid}/items/{itemId:guid}", RemoveItemAsync);
        group.MapDelete("/{id:guid}/media/{mediaId:guid}", RemoveMediaAsync)
            .WithName("RemovePlaylistMedia")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static Guid RequireProfileId(ClaimsPrincipal user) => user.GetProfileId() ?? Guid.Empty;

    private static async Task<IResult> GetPlaylistsAsync(ClaimsPrincipal user, IPlaylistManager manager) =>
        Results.Ok(await manager.GetPlaylistsAsync(RequireProfileId(user), user.GetPlaylistAccessFilter()));

    // 404 covers deleted, unshared and never-existed alike. A viewer who had a
    // shared playlist open when its owner removed it is told it is no longer
    // available; nothing is kept around to spare them that.
    private static async Task<IResult> GetPlaylistAsync(Guid id, ClaimsPrincipal user, IPlaylistManager manager)
    {
        var playlist = await manager.GetPlaylistDetailsAsync(id, RequireProfileId(user), user.GetPlaylistAccessFilter());
        return playlist != null ? Results.Ok(playlist) : Results.NotFound();
    }

    private static async Task<IResult> GetSharedAsync(ClaimsPrincipal user, IPlaylistManager manual, ISmartPlaylistManager smart)
    {
        var viewer = RequireProfileId(user);
        var access = user.GetPlaylistAccessFilter();
        return Results.Ok(new SharedPlaylistsVM
        {
            Manual = await manual.GetSharedByOthersAsync(viewer, access),
            Smart = await smart.GetSharedByOthersAsync(viewer, access)
        });
    }

    private static async Task<IResult> SetSharingAsync(Guid id, [FromBody] SetPlaylistSharingRequest req, ClaimsPrincipal user, IPlaylistManager manager) =>
        await manager.SetSharedAsync(id, RequireProfileId(user), req.IsShared) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> CopyAsync(Guid id, ClaimsPrincipal user, IPlaylistManager manager)
    {
        var copyId = await manager.CopyPlaylistAsync(id, RequireProfileId(user), user.GetPlaylistAccessFilter());
        return copyId.HasValue ? Results.Ok(new CreatePlaylistResponse { Id = copyId.Value }) : Results.NotFound();
    }

    private static async Task<IResult> GetPlaylistsContainingAsync(Guid mediaId, ClaimsPrincipal user, IPlaylistManager manager) =>
        Results.Ok(await manager.GetPlaylistsContainingItemAsync(RequireProfileId(user), mediaId));

    private static async Task<IResult> CreatePlaylistAsync([FromBody] CreatePlaylistRequest req, ClaimsPrincipal user, IPlaylistManager manager) =>
        Results.Ok(new CreatePlaylistResponse { Id = await manager.CreatePlaylistAsync(RequireProfileId(user), req.Name, req.Description, req.MediaType) });

    private static async Task<IResult> AddItemAsync(Guid id, Guid mediaId, ClaimsPrincipal user, IPlaylistManager manager)
    {
        await manager.AddToPlaylistAsync(id, RequireProfileId(user), mediaId);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllUnplayedAsync(Guid id, ClaimsPrincipal user, IPlaylistManager manager)
    {
        await manager.MarkAllUnplayedAsync(id, RequireProfileId(user));
        return Results.NoContent();
    }

    private static async Task<IResult> UpdatePlaylistAsync(Guid id, [FromBody] UpdatePlaylistRequest req, ClaimsPrincipal user, IPlaylistManager manager)
    {
        await manager.UpdatePlaylistDetailsAsync(id, RequireProfileId(user), req.Name, req.Description);
        return Results.NoContent();
    }

    private static async Task<IResult> ReorderAsync(Guid id, [FromBody] ReorderPlaylistRequest req, ClaimsPrincipal user, IPlaylistManager manager)
    {
        await manager.ReorderPlaylistAsync(id, RequireProfileId(user), req.PlaylistItemIds);
        return Results.NoContent();
    }

    private static async Task<IResult> DeletePlaylistAsync(Guid id, ClaimsPrincipal user, IPlaylistManager manager)
    {
        await manager.DeletePlaylistAsync(id, RequireProfileId(user));
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveItemAsync(Guid id, Guid itemId, ClaimsPrincipal user, IPlaylistManager manager)
    {
        await manager.RemoveFromPlaylistAsync(id, RequireProfileId(user), itemId);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveMediaAsync(Guid id, Guid mediaId, ClaimsPrincipal user, IPlaylistManager manager)
    {
        await manager.RemoveMediaFromPlaylistAsync(id, RequireProfileId(user), mediaId);
        return Results.NoContent();
    }
}

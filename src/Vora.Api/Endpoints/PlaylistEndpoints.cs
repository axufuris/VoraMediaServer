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

        group.MapGet("/", GetPlaylistsAsync);
        group.MapGet("/{id:guid}", GetPlaylistAsync);
        group.MapGet("/contains/{mediaId:guid}", GetPlaylistsContainingAsync);

        group.MapPost("/", CreatePlaylistAsync);
        group.MapPost("/{id:guid}/items/{mediaId:guid}", AddItemAsync);
        group.MapPost("/{id:guid}/unwatch-all", MarkAllUnplayedAsync);

        group.MapPut("/{id:guid}", UpdatePlaylistAsync);
        group.MapPut("/{id:guid}/reorder", ReorderAsync);

        group.MapDelete("/{id:guid}", DeletePlaylistAsync);
        group.MapDelete("/{id:guid}/items/{itemId:guid}", RemoveItemAsync);
        group.MapDelete("/{id:guid}/media/{mediaId:guid}", RemoveMediaAsync);

        return group;
    }

    private static Guid RequireProfileId(ClaimsPrincipal user) => user.GetProfileId() ?? Guid.Empty;

    private static async Task<IResult> GetPlaylistsAsync(ClaimsPrincipal user, IPlaylistManager manager) =>
        Results.Ok(await manager.GetPlaylistsAsync(RequireProfileId(user)));

    private static async Task<IResult> GetPlaylistAsync(Guid id, ClaimsPrincipal user, IPlaylistManager manager)
    {
        var playlist = await manager.GetPlaylistDetailsAsync(id, RequireProfileId(user));
        return playlist != null ? Results.Ok(playlist) : Results.NotFound();
    }

    private static async Task<IResult> GetPlaylistsContainingAsync(Guid mediaId, ClaimsPrincipal user, IPlaylistManager manager) =>
        Results.Ok(await manager.GetPlaylistsContainingItemAsync(RequireProfileId(user), mediaId));

    private static async Task<IResult> CreatePlaylistAsync([FromBody] CreatePlaylistRequest req, ClaimsPrincipal user, IPlaylistManager manager) =>
        Results.Ok(new { Id = await manager.CreatePlaylistAsync(RequireProfileId(user), req.Name, req.Description, req.MediaType) });

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

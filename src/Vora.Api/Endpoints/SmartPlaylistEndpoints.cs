using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Playlists;

namespace Vora.Api.Endpoints;

public static class SmartPlaylistEndpoints
{
    public static IEndpointRouteBuilder MapSmartPlaylistEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/smart-playlists").WithTags("SmartPlaylists");

        group.MapGet("/", ListAsync)
            .RequireAuthorization()
            .WithName("ListSmartPlaylists")
            .Produces<IEnumerable<SmartPlaylistSummaryVM>>(StatusCodes.Status200OK);
        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization()
            .WithName("GetSmartPlaylist")
            .Produces<SmartPlaylistDetailVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/", CreateAsync).RequireAuthorization();
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization();
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization();
        group.MapGet("/{id:guid}/items", GetTracksAsync)
            .RequireAuthorization()
            .WithName("ListSmartPlaylistItems")
            .Produces<SmartPlaylistItemsVM>(StatusCodes.Status200OK);
        group.MapPost("/preview", PreviewAsync).RequireAuthorization();

        return routes;
    }

    private static MusicAccessFilter BuildFilter(ClaimsPrincipal user) => new()
    {
        HasAllLibraryAccess = user.HasAllLibraryAccess(),
        AllowedLibraryIds = user.GetAllowedLibraryIds(),
        HasAllRatings = user.HasAllContentRatings(),
        AllowedRatings = user.GetAllowedMusicRatings(),
        BlockUnratedContent = user.BlockUnratedContent()
    };

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISmartPlaylistManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var list = await manager.ListAsync(profileId.Value, BuildFilter(user));
        return Results.Ok(list);
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ISmartPlaylistManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var detail = await manager.GetAsync(id, profileId.Value, BuildFilter(user));
        if (detail == null) return Results.NotFound();
        return Results.Ok(detail);
    }

    private static async Task<IResult> CreateAsync([FromBody] SmartPlaylistSaveRequest request, ClaimsPrincipal user, ISmartPlaylistManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Name is required." });
        var summary = await manager.CreateAsync(profileId.Value, request);
        return Results.Ok(summary);
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] SmartPlaylistSaveRequest request, ClaimsPrincipal user, ISmartPlaylistManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Name is required." });
        var summary = await manager.UpdateAsync(id, profileId.Value, request);
        if (summary == null) return Results.NotFound();
        return Results.Ok(summary);
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal user, ISmartPlaylistManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        await manager.DeleteAsync(id, profileId.Value);
        return Results.NoContent();
    }

    private static async Task<IResult> GetTracksAsync(Guid id, ClaimsPrincipal user, ISmartPlaylistManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var items = await manager.GetItemsAsync(id, profileId.Value, BuildFilter(user));
        return Results.Ok(items);
    }

    private static async Task<IResult> PreviewAsync([FromBody] SmartPlaylistPreviewRequest request, ClaimsPrincipal user, ISmartPlaylistManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var count = await manager.PreviewCountAsync(profileId.Value, BuildFilter(user), request.MediaType, request.Definition ?? new SmartPlaylistDefinition());
        return Results.Ok(new { count });
    }

    public sealed class SmartPlaylistPreviewRequest
    {
        public PlaylistMediaType MediaType { get; set; } = PlaylistMediaType.Music;
        public SmartPlaylistDefinition? Definition { get; set; }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;

namespace Vora.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization("AdminOnly");

        var dedupeGroup = group.MapGroup("/dedupe");

        dedupeGroup.MapGet("/", GetDuplicatesAsync)
            .Produces<List<DedupeGroupVM>>(StatusCodes.Status200OK);

        dedupeGroup.MapDelete("/{partId:guid}", DeleteDuplicatePartAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        dedupeGroup.MapGet("/settings", GetGlobalSettingsAsync)
            .Produces<DedupeSettingsVM>(StatusCodes.Status200OK);

        dedupeGroup.MapPut("/settings", UpdateGlobalSettingsAsync)
            .Produces<DedupeSettingsVM>(StatusCodes.Status200OK);

        dedupeGroup.MapGet("/settings/defaults", GetDefaultSettingsAsync)
            .Produces<DedupeSettingsVM>(StatusCodes.Status200OK);

        dedupeGroup.MapGet("/settings/library/{libraryId:guid}", GetLibrarySettingsAsync)
            .Produces<DedupeSettingsVM>(StatusCodes.Status200OK);

        dedupeGroup.MapPut("/settings/library/{libraryId:guid}", UpdateLibrarySettingsAsync)
            .Produces<DedupeSettingsVM>(StatusCodes.Status200OK);

        dedupeGroup.MapDelete("/settings/library/{libraryId:guid}", DeleteLibrarySettingsAsync)
            .Produces(StatusCodes.Status204NoContent);

        dedupeGroup.MapGet("/settings/library-overrides", GetLibraryOverridesAsync)
            .Produces<List<DedupeSettingsVM>>(StatusCodes.Status200OK);

        dedupeGroup.MapGet("/ignored", GetIgnoredGroupsAsync)
            .Produces<List<DedupeIgnoredGroupVM>>(StatusCodes.Status200OK);

        dedupeGroup.MapPost("/ignored", IgnoreGroupAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        dedupeGroup.MapDelete("/ignored/{ignoredGroupId:guid}", UnignoreGroupAsync)
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }

    private static async Task<IResult> GetDuplicatesAsync(IMediaDedupeManager manager)
    {
        var duplicates = await manager.GetDuplicateMediaAsync();
        return Results.Ok(duplicates);
    }

    private static async Task<IResult> DeleteDuplicatePartAsync(Guid partId, bool deletePhysical, IMediaDedupeManager manager)
    {
        try
        {
            await manager.DeleteDuplicatePartAsync(partId, deletePhysical);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { Message = ex.Message });
        }
    }

    private static async Task<IResult> GetGlobalSettingsAsync(IMediaDedupeManager manager)
    {
        var settings = await manager.GetGlobalSettingsAsync();
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateGlobalSettingsAsync([FromBody] DedupeSettingsVM settings, IMediaDedupeManager manager)
    {
        var saved = await manager.SaveGlobalSettingsAsync(settings);
        return Results.Ok(saved);
    }

    private static async Task<IResult> GetDefaultSettingsAsync(IMediaDedupeManager manager)
    {
        var defaults = await manager.GetDefaultSettingsAsync();
        return Results.Ok(defaults);
    }

    private static async Task<IResult> GetLibrarySettingsAsync(Guid libraryId, IMediaDedupeManager manager)
    {
        var settings = await manager.GetEffectiveLibrarySettingsAsync(libraryId);
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateLibrarySettingsAsync(Guid libraryId, [FromBody] DedupeSettingsVM settings, IMediaDedupeManager manager)
    {
        var saved = await manager.SaveLibraryOverrideAsync(libraryId, settings);
        return Results.Ok(saved);
    }

    private static async Task<IResult> DeleteLibrarySettingsAsync(Guid libraryId, IMediaDedupeManager manager)
    {
        await manager.ClearLibraryOverrideAsync(libraryId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetLibraryOverridesAsync(IMediaDedupeManager manager)
    {
        var overrides = await manager.GetAllLibraryOverridesAsync();
        return Results.Ok(overrides);
    }

    private static async Task<IResult> GetIgnoredGroupsAsync(IMediaDedupeManager manager)
    {
        var ignored = await manager.GetIgnoredGroupsAsync();
        return Results.Ok(ignored);
    }

    private static async Task<IResult> IgnoreGroupAsync([FromBody] IgnoreGroupRequest request, ClaimsPrincipal user, IMediaDedupeManager manager)
    {
        if (request.MediaItemId == Guid.Empty || string.IsNullOrWhiteSpace(request.Resolution))
        {
            return Results.BadRequest(new { Message = "MediaItemId and Resolution are required." });
        }

        var profileId = user.GetProfileId()?.ToString();
        await manager.IgnoreGroupAsync(request.MediaItemId, request.Resolution, profileId, request.Note);
        return Results.NoContent();
    }

    private static async Task<IResult> UnignoreGroupAsync(Guid ignoredGroupId, IMediaDedupeManager manager)
    {
        await manager.UnignoreGroupAsync(ignoredGroupId);
        return Results.NoContent();
    }

    public class IgnoreGroupRequest
    {
        public Guid MediaItemId { get; set; }
        public string Resolution { get; set; } = string.Empty;
        public string? Note { get; set; }
    }
}

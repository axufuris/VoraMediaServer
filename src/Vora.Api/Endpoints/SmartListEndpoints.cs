using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.SmartLists;
using Vora.Application.SmartLists.Requests;
using Vora.Application.SmartLists.ViewModels;

namespace Vora.Api.Endpoints;

public class ReorderSmartListsRequest
{
    public List<Guid> ListIds { get; set; } = new();
}

public class RestoreSmartListDefaultsResponse
{
    public int Restored { get; set; }
}

public static class SmartListEndpoints
{
    public static IEndpointRouteBuilder MapSmartListEndpoints(this IEndpointRouteBuilder routes)
    {
        MapClientSmartListEndpoints(routes);
        MapAdminSmartListEndpoints(routes);
        return routes;
    }

    private static void MapClientSmartListEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/smartlists").WithTags("Smart Lists").RequireAuthorization();

        group.MapGet("/active", GetActiveListsAsync)
            .WithName("ListActiveSmartLists")
            .Produces<IEnumerable<SmartListClientVM>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}/items", GetListItemsAsync)
            .WithName("GetSmartListItems")
            .Produces<IEnumerable<LibraryItemVM>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}/entries", GetListEntriesAsync)
            .WithName("GetSmartListEntries")
            .Produces<IEnumerable<SmartListEntryVM>>(StatusCodes.Status200OK);
    }

    private static void MapAdminSmartListEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/smartlists").WithTags("Smart Lists (Admin)").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAllAdminListsAsync)
            .WithName("ListAdminSmartLists")
            .Produces<IEnumerable<SmartListAdminVM>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateListAsync)
            .WithName("CreateSmartList")
            .Produces<Guid>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/reorder", ReorderListsAsync)
            .WithName("ReorderSmartLists")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/defaults", GetDefaultListsAsync)
            .WithName("ListSmartListDefaults")
            .Produces<IEnumerable<SmartListDefaultVM>>(StatusCodes.Status200OK);

        group.MapPost("/defaults/restore", RestoreDefaultListsAsync)
            .WithName("RestoreSmartListDefaults")
            .Produces<RestoreSmartListDefaultsResponse>(StatusCodes.Status200OK);

        group.MapPut("/{id:guid}", UpdateListAsync)
            .WithName("UpdateSmartList")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteListAsync)
            .WithName("DeleteSmartList")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetActiveListsAsync(ClaimsPrincipal user, ISmartListManager manager)
    {
        var lists = await manager.GetActiveSmartListsAsync(user.IsAdmin());
        return Results.Ok(lists);
    }

    private static async Task<IResult> GetListItemsAsync(Guid id, ClaimsPrincipal user, ISmartListManager manager)
    {
        var items = await manager.GetSmartListItemsAsync(id, Viewer(user));
        return Results.Ok(items);
    }

    private static async Task<IResult> GetListEntriesAsync(Guid id, ClaimsPrincipal user, ISmartListManager manager)
    {
        var entries = await manager.GetSmartListEntriesAsync(id, Viewer(user));
        return Results.Ok(entries);
    }

    private static SmartListViewer Viewer(ClaimsPrincipal user) => new(
        user.GetAccountId(),
        user.GetProfileId(),
        user.IsAdmin(),
        user.HasAllLibraryAccess(),
        user.GetAllowedLibraryIds(),
        user.GetAllowedMovieRatings(),
        user.GetAllowedTvRatings(),
        user.BlockUnratedContent(),
        user.GetMusicAccessFilter());

    private static async Task<IResult> GetAllAdminListsAsync(ISmartListManager manager)
    {
        var lists = await manager.GetAllAdminListsAsync();
        return Results.Ok(lists);
    }

    private static async Task<IResult> GetDefaultListsAsync(ISmartListManager manager)
    {
        var defaults = await manager.GetDefaultListsAsync();
        return Results.Ok(defaults);
    }

    private static async Task<IResult> RestoreDefaultListsAsync(ISmartListManager manager)
    {
        var restored = await manager.RestoreDefaultListsAsync();
        return Results.Ok(new RestoreSmartListDefaultsResponse { Restored = restored });
    }

    private static async Task<IResult> CreateListAsync([FromBody] SmartListSaveRequest request, ISmartListManager manager)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.BadRequest("A smart list needs a title.");
        }

        var newId = await manager.CreateListAsync(request);
        return Results.Created($"/api/smartlists/{newId}", newId);
    }

    private static async Task<IResult> ReorderListsAsync([FromBody] ReorderSmartListsRequest request, ISmartListManager manager)
    {
        await manager.ReorderListsAsync(request.ListIds);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateListAsync(Guid id, [FromBody] SmartListSaveRequest request, ISmartListManager manager)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.BadRequest("A smart list needs a title.");
        }

        var success = await manager.UpdateListAsync(id, request);
        return success ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteListAsync(Guid id, ISmartListManager manager)
    {
        var deleted = await manager.DeleteListAsync(id);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}

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
            .Produces<IEnumerable<SmartListClientVM>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}/items", GetListItemsAsync)
            .Produces<IEnumerable<LibraryItemVM>>(StatusCodes.Status200OK);
    }

    private static void MapAdminSmartListEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/smartlists").WithTags("Smart Lists (Admin)").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAllAdminListsAsync)
            .Produces<IEnumerable<SmartListAdminVM>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateListAsync)
            .Produces(StatusCodes.Status201Created);

        group.MapPut("/reorder", ReorderListsAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPut("/{id:guid}", UpdateListAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteListAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> GetActiveListsAsync(ClaimsPrincipal user, ISmartListManager manager)
    {
        var lists = await manager.GetActiveSmartListsAsync(Guid.Empty, user.IsAdmin());
        return Results.Ok(lists);
    }

    private static async Task<IResult> GetListItemsAsync(Guid id, ClaimsPrincipal user, ISmartListManager manager)
    {
        var items = await manager.GetSmartListItemsAsync(
            id,
            user.GetProfileId(),
            null,
            user.HasAllLibraryAccess(),
            user.GetAllowedLibraryIds(),
            user.HasAllContentRatings(),
            user.GetAllowedMovieRatings(),
            user.GetAllowedTvRatings(),
            user.BlockUnratedContent());
        return Results.Ok(items);
    }

    private static async Task<IResult> GetAllAdminListsAsync(ISmartListManager manager)
    {
        var lists = await manager.GetAllAdminListsAsync();
        return Results.Ok(lists);
    }

    private static async Task<IResult> CreateListAsync([FromBody] SmartListSaveRequest request, ISmartListManager manager)
    {
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
        var success = await manager.UpdateListAsync(id, request);
        return success ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteListAsync(Guid id, ISmartListManager manager)
    {
        try
        {
            await manager.DeleteListAsync(id);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Collections;
using Vora.Application.Collections.Requests;
using Vora.Application.Collections.ViewModels;

namespace Vora.Api.Endpoints;

public static class CollectionEndpoints
{
    public static RouteGroupBuilder MapCollectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/collections").WithTags("Collections").RequireAuthorization();

        group.MapGet("/", GetAllCollectionsAsync)
            .WithName("ListAllCollections")
            .Produces<IEnumerable<CollectionSummaryVM>>(StatusCodes.Status200OK);

        group.MapGet("/global", GetGlobalCollectionsAsync)
            .WithName("ListGlobalCollections")
            .Produces<IEnumerable<CollectionSummaryVM>>(StatusCodes.Status200OK);

        group.MapGet("/library/{libraryId:guid}", GetLibraryCollectionsAsync)
            .WithName("ListLibraryCollections")
            .Produces<IEnumerable<CollectionSummaryVM>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetCollectionAsync)
            .WithName("GetCollectionDetails")
            .Produces<CollectionDetailsVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateCollectionAsync)
            .Produces(StatusCodes.Status201Created);

        group.MapPost("/{id:guid}/items/{mediaId:guid}", AddItemAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{id:guid}/sync-chronology", ApplyChronologyAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", UpdateCollectionAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPut("/{id:guid}/items/reorder", ReorderItemsAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/{id:guid}/items/{mediaId:guid}", RemoveItemAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/{id:guid}", DeleteCollectionAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> GetAllCollectionsAsync(ClaimsPrincipal user, ICollectionManager manager)
    {
        var collections = await manager.GetAllCollectionsAsync(user.HasAllLibraryAccess(), user.GetAllowedLibraryIds());
        return Results.Ok(collections);
    }

    private static async Task<IResult> GetGlobalCollectionsAsync(ClaimsPrincipal user, ICollectionManager manager)
    {
        var collections = await manager.GetGlobalCollectionsAsync(user.HasAllLibraryAccess(), user.GetAllowedLibraryIds());
        return Results.Ok(collections);
    }

    private static async Task<IResult> GetLibraryCollectionsAsync(Guid libraryId, ClaimsPrincipal user, ICollectionManager manager)
    {
        var collections = await manager.GetLibraryCollectionsAsync(libraryId, user.HasAllLibraryAccess(), user.GetAllowedLibraryIds());
        return Results.Ok(collections);
    }

    private static async Task<IResult> GetCollectionAsync(Guid id, ClaimsPrincipal user, ICollectionManager manager)
    {
        var collection = await manager.GetCollectionDetailsAsync(id, user.GetProfileId(), user.HasAllLibraryAccess(), user.GetAllowedLibraryIds());
        return collection != null ? Results.Ok(collection) : Results.NotFound();
    }

    private static async Task<IResult> CreateCollectionAsync([FromBody] CreateCollectionRequest request, ICollectionManager manager)
    {
        var id = await manager.CreateCollectionAsync(request);
        return Results.Created($"/api/collections/{id}", new { Id = id });
    }

    private static async Task<IResult> AddItemAsync(Guid id, Guid mediaId, ICollectionManager manager)
    {
        await manager.AddMediaToCollectionAsync(id, mediaId);
        return Results.NoContent();
    }

    private static async Task<IResult> ApplyChronologyAsync(Guid id, CollectionOrderingService orderingService)
    {
        try
        {
            await orderingService.ApplyChronologicalOrderAsync(id);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { Error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateCollectionAsync(Guid id, [FromBody] UpdateCollectionRequest request, ICollectionManager manager)
    {
        await manager.UpdateCollectionAsync(id, request);
        return Results.NoContent();
    }

    private static async Task<IResult> ReorderItemsAsync(Guid id, [FromBody] ReorderCollectionRequest request, ICollectionManager manager)
    {
        await manager.ReorderCollectionItemsAsync(id, request.MediaItemIds);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveItemAsync(Guid id, Guid mediaId, ICollectionManager manager)
    {
        await manager.RemoveMediaFromCollectionAsync(id, mediaId);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCollectionAsync(Guid id, ICollectionManager manager)
    {
        try
        {
            await manager.DeleteCollectionAsync(id);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { ex.Message });
        }
    }
}

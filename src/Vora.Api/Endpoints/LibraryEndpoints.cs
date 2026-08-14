using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Libraries;
using Vora.Application.Libraries.Requests;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media;
using Vora.Application.Tasks;

namespace Vora.Api.Endpoints;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder routes)
    {
        MapReadEndpoints(routes);
        MapAdminEndpoints(routes);
        return routes;
    }

    private static void MapReadEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/libraries").WithTags("Libraries").RequireAuthorization();

        group.MapGet("/", GetLibrariesAsync)
            .WithName("ListLibraries")
            .Produces<IEnumerable<MediaLibraryVM>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetLibraryAsync)
            .WithName("GetLibrary")
            .Produces<MediaLibraryVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{libraryId:guid}/media", GetLibraryContentAsync)
            .WithName("ListLibraryItems")
            .Produces<IEnumerable<LibraryItemVM>>(StatusCodes.Status200OK);
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/libraries").WithTags("Libraries (Admin)").RequireAuthorization("AdminOnly");

        group.MapPost("/", CreateLibraryAsync)
            .Produces(StatusCodes.Status201Created);

        group.MapPost("/{id:guid}/scan", QueueScanAsync)
            .Produces(StatusCodes.Status202Accepted);

        group.MapPost("/{id:guid}/metadata", QueueRefreshMetadataAsync)
            .Produces(StatusCodes.Status202Accepted);

        group.MapPost("/{id:guid}/analyze", QueueAnalyzeAsync)
            .Produces(StatusCodes.Status202Accepted);

        group.MapGet("/{id:guid}/marker-coverage", GetMarkerCoverageAsync)
            .Produces<MarkerCoverageVM>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/ratings", QueueRefreshRatingsAsync)
            .Produces(StatusCodes.Status202Accepted);

        group.MapPost("/{id:guid}/watchfolder", ToggleWatchfolderAsync)
            .Produces(StatusCodes.Status200OK);

        group.MapPut("/{id:guid}", UpdateLibraryAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/{id:guid}", DeleteLibraryAsync)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> GetLibrariesAsync(ClaimsPrincipal user, ILibraryManager manager)
    {
        var libraries = await manager.GetLibrariesAsync(user.HasAllLibraryAccess(), user.GetAllowedLibraryIds());
        return Results.Ok(libraries);
    }

    private static async Task<IResult> GetLibraryAsync(Guid id, ILibraryManager manager)
    {
        var library = await manager.GetLibraryByIdAsync(id);
        return library == null ? Results.NotFound() : Results.Ok(library);
    }

    private static async Task<IResult> GetLibraryContentAsync(Guid libraryId, ClaimsPrincipal user, IMediaManager manager)
    {
        var items = await manager.GetLibraryContentAsync(
            libraryId,
            user.GetProfileId(),
            user.HasAllLibraryAccess(),
            user.GetAllowedLibraryIds(),
            user.HasAllContentRatings(),
            user.GetAllowedMovieRatings(),
            user.GetAllowedTvRatings(),
            user.BlockUnratedContent());
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateLibraryAsync([FromBody] CreateLibraryRequest request, ILibraryManager manager)
    {
        var id = await manager.CreateLibraryAsync(request);
        return Results.Created($"/api/libraries/{id}", new { Id = id });
    }

    private static IResult QueueScanAsync(Guid id, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueScanLibrary(id);
        return Results.Accepted();
    }

    private static IResult QueueRefreshMetadataAsync(Guid id, [FromQuery] bool force, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueRefreshLibraryMetadata(id, null, forceOverride: force);
        return Results.Accepted();
    }

    private static IResult QueueAnalyzeAsync(Guid id, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueAnalyzeLibraryMediaContent(id, forceOverride: false);
        return Results.Accepted();
    }

    private static async Task<IResult> GetMarkerCoverageAsync(Guid id, IMediaManager manager)
    {
        var coverage = await manager.GetLibraryMarkerCoverageAsync(id);
        return Results.Ok(coverage);
    }

    private static IResult QueueRefreshRatingsAsync(Guid id, [FromQuery] bool force, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueRefreshLibraryRatings(id, forceOverride: force);
        return Results.Accepted();
    }

    private static async Task<IResult> ToggleWatchfolderAsync(Guid id, [FromQuery] bool enable, ILibraryManager manager)
    {
        await manager.ToggleWatchingAsync(id, enable);
        return Results.Ok();
    }

    private static async Task<IResult> UpdateLibraryAsync(Guid id, [FromBody] UpdateLibraryRequest request, ILibraryManager manager)
    {
        await manager.UpdateLibraryAsync(id, request);
        return Results.NoContent();
    }

    private static IResult DeleteLibraryAsync(Guid id, Vora.Application.Tasks.ITaskQueueManager taskQueue)
    {
        taskQueue.QueueDeleteLibrary(id);
        return Results.Accepted();
    }
}

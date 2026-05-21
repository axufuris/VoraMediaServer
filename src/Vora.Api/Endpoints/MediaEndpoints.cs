using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Media.ViewModels;
using Vora.Application.Tasks;

namespace Vora.Api.Endpoints;

public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder routes)
    {
        MapMediaItemEndpoints(routes);
        MapSeasonEndpoints(routes);
        MapMetadataEndpoints(routes);
        return routes;
    }

    private static void MapMediaItemEndpoints(IEndpointRouteBuilder routes)
    {
        var readGroup = routes.MapGroup("/api/media").WithTags("Media").RequireAuthorization();

        readGroup.MapGet("/{id:guid}", GetMediaItemAsync)
            .Produces<MediaItemVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        readGroup.MapGet("/{id:guid}/up-next", GetUpNextAsync);
        readGroup.MapPost("/{id:guid}/played", SetPlayedAsync);
        readGroup.MapPut("/{id:guid}/rating", SetRatingAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        var adminGroup = routes.MapGroup("/api/media").WithTags("Media (Admin)").RequireAuthorization("AdminOnly");

        adminGroup.MapPost("/{id:guid}/scan", QueueScanAsync)
            .Produces(StatusCodes.Status202Accepted);

        adminGroup.MapPost("/{id:guid}/metadata", QueueRefreshMetadataAsync)
            .Produces(StatusCodes.Status202Accepted);

        adminGroup.MapPost("/{id:guid}/analyze", QueueAnalyzeAsync)
            .Produces(StatusCodes.Status202Accepted);

        adminGroup.MapPost("/{id:guid}/artwork", QueueRefreshArtworkAsync)
            .Produces(StatusCodes.Status202Accepted);

        adminGroup.MapPut("/{id:guid}", UpdateMediaAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        adminGroup.MapDelete("/{id:guid}", DeleteMediaAsync)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapSeasonEndpoints(IEndpointRouteBuilder routes)
    {
        var readGroup = routes.MapGroup("/api/seasons").WithTags("Seasons").RequireAuthorization();

        readGroup.MapGet("/{id:guid}", GetSeasonAsync)
            .Produces<SeasonDetailsVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        var adminGroup = routes.MapGroup("/api/seasons").WithTags("Seasons (Admin)").RequireAuthorization("AdminOnly");

        adminGroup.MapPut("/{id:guid}", UpdateSeasonAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapMetadataEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/metadata").WithTags("Global Metadata").RequireAuthorization("AdminOnly");

        group.MapPost("/actors/refresh", QueueRefreshAllActorsAsync)
            .Produces(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> GetMediaItemAsync(Guid id, ClaimsPrincipal user, IMediaManager manager)
    {
        var item = await manager.GetMediaItemAsync(
            id,
            user.GetProfileId(),
            user.HasAllLibraryAccess(),
            user.GetAllowedLibraryIds(),
            user.HasAllContentRatings(),
            user.GetAllowedMovieRatings(),
            user.GetAllowedTvRatings(),
            user.BlockUnratedContent());

        return item == null
            ? Results.NotFound(new { Message = "Media item not found." })
            : Results.Ok(item);
    }

    private static async Task<IResult> GetUpNextAsync(
        Guid id,
        [FromQuery] string? contextType,
        [FromQuery] Guid? contextId,
        IUserMediaStateManager manager)
    {
        var result = await manager.GetUpNextAsync(id, contextType, contextId);
        return Results.Ok(result);
    }

    private static async Task<IResult> SetPlayedAsync(
        Guid id,
        [FromQuery] bool isPlayed,
        ClaimsPrincipal user,
        IUserMediaStateManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null)
        {
            return Results.Unauthorized();
        }

        await manager.SetMediaPlayedStateAsync(id, profileId.Value, isPlayed);
        return Results.Ok();
    }

    public sealed class SetRatingRequest
    {
        public decimal? Rating { get; set; }
    }

    private static async Task<IResult> SetRatingAsync(
        Guid id,
        [FromBody] SetRatingRequest request,
        ClaimsPrincipal user,
        IUserMediaStateManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();

        try
        {
            var result = await manager.SetMediaRatingAsync(id, profileId.Value, request.Rating, user.IsAdmin());
            if (!result.Found) return Results.NotFound(new { Message = "Media item not found." });
            return Results.NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IResult QueueScanAsync(Guid id, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueScanMediaItem(id);
        return Results.Accepted();
    }

    private static IResult QueueRefreshMetadataAsync(Guid id, [FromQuery] bool force, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueRefreshMediaItemMetadata(id, null, forceOverride: force);
        return Results.Accepted();
    }

    private static IResult QueueAnalyzeAsync(Guid id, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueAnalyzeMediaItemContent(id);
        return Results.Accepted();
    }

    private static IResult QueueRefreshArtworkAsync(Guid id, [FromQuery] bool force, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueRefreshMediaItemArtwork(id, forceOverride: force);
        return Results.Accepted();
    }

    private static async Task<IResult> UpdateMediaAsync(Guid id, [FromBody] UpdateMediaRequest request, IMediaManager manager)
    {
        try
        {
            await manager.UpdateMediaMetadataAsync(id, request);
            return Results.NoContent();
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { Message = "Media item not found." });
        }
    }

    private static async Task<IResult> DeleteMediaAsync(Guid id, IMediaManager manager)
    {
        await manager.DeleteMediaAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> GetSeasonAsync(Guid id, IMediaManager manager)
    {
        var season = await manager.GetSeasonDetailsAsync(id);
        return season == null ? Results.NotFound() : Results.Ok(season);
    }

    private static async Task<IResult> UpdateSeasonAsync(Guid id, [FromBody] UpdateSeasonRequest request, IMediaManager manager)
    {
        try
        {
            await manager.UpdateSeasonMetadataAsync(id, request);
            return Results.NoContent();
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { Message = "Season not found." });
        }
    }

    private static IResult QueueRefreshAllActorsAsync(ITaskQueueManager taskQueue)
    {
        taskQueue.QueueRefreshAllActorMetadata();
        return Results.Accepted();
    }
}

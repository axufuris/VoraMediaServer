using Microsoft.AspNetCore.Mvc;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;

namespace Vora.Api.Endpoints;

public class ThumbnailsLockDto
{
    public bool Locked { get; set; }
}

public class ThumbnailCoverageDto
{
    public int Total { get; set; }
    public int WithThumbnails { get; set; }
}

public static class VideoThumbnailEndpoints
{
    public static IEndpointRouteBuilder MapVideoThumbnailEndpoints(this IEndpointRouteBuilder routes)
    {
        var clientGroup = routes.MapGroup("/api/media").WithTags("Video Thumbnails").RequireAuthorization();

        clientGroup.MapGet("/{id:guid}/thumbnails.vtt", ServeVttAsync)
            .Produces(StatusCodes.Status200OK, contentType: "text/vtt")
            .Produces(StatusCodes.Status404NotFound);

        clientGroup.MapGet("/{id:guid}/thumbnails.jpg", ServeSpriteAsync)
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .Produces(StatusCodes.Status404NotFound);

        var adminGroup = routes.MapGroup("/api/media").WithTags("Video Thumbnails (Admin)").RequireAuthorization("AdminOnly");

        adminGroup.MapPost("/{id:guid}/thumbnails/regenerate", QueueRegenerateMediaItemAsync)
            .Produces(StatusCodes.Status202Accepted);

        adminGroup.MapGet("/{id:guid}/thumbnails/lock", GetThumbnailsLockedAsync)
            .Produces<ThumbnailsLockDto>(StatusCodes.Status200OK);

        adminGroup.MapPut("/{id:guid}/thumbnails/lock", SetThumbnailsLockedAsync)
            .Produces<ThumbnailsLockDto>(StatusCodes.Status200OK);

        var libraryAdminGroup = routes.MapGroup("/api/libraries").WithTags("Video Thumbnails (Admin)").RequireAuthorization("AdminOnly");

        libraryAdminGroup.MapPost("/{id:guid}/thumbnails/regenerate", QueueRegenerateLibraryAsync)
            .Produces(StatusCodes.Status202Accepted);

        libraryAdminGroup.MapGet("/{id:guid}/thumbnails/coverage", GetLibraryCoverageAsync)
            .Produces<ThumbnailCoverageDto>(StatusCodes.Status200OK);

        return routes;
    }

    private static IResult ServeVttAsync(Guid id, IVideoThumbnailStorageService storage, HttpContext httpContext)
    {
        var path = storage.GetVttPath(id);
        if (!File.Exists(path)) return Results.NotFound();

        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        var etag = $"\"{lastWriteUtc.Ticks:x}\"";
        httpContext.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.File(path, "text/vtt", entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(etag), lastModified: lastWriteUtc);
    }

    private static IResult ServeSpriteAsync(Guid id, IVideoThumbnailStorageService storage, HttpContext httpContext)
    {
        var path = storage.GetSpritePath(id);
        if (!File.Exists(path)) return Results.NotFound();

        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        var etag = $"\"{lastWriteUtc.Ticks:x}\"";
        httpContext.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.File(path, "image/webp", entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(etag), lastModified: lastWriteUtc);
    }

    private static async Task<IResult> QueueRegenerateMediaItemAsync(Guid id, ITaskQueueManager taskQueue, IMediaRepository mediaRepository)
    {
        var title = await mediaRepository.GetProjectedAsync(id, m => m.Title);
        taskQueue.QueueGenerateMediaItemVideoThumbnails(id, title, forceOverride: true);
        return Results.Accepted();
    }

    private static async Task<IResult> QueueRegenerateLibraryAsync(Guid id, [FromQuery] bool force, ITaskQueueManager taskQueue, ILibraryRepository libraryRepository)
    {
        var name = await libraryRepository.GetProjectedByIdAsync(id, l => l.Name);
        // "Regenerate missing" (force=false) fills the gaps: items that already have
        // current-version thumbnails are skipped and only missing (or settings-stale,
        // via the version check) items are generated — so a run that stopped partway
        // (the task queue is in-memory and doesn't resume across a restart) can be
        // continued instead of redoing everything. "Regenerate all" (force=true)
        // redoes every item regardless, for when the thumbnails themselves are wrong.
        taskQueue.QueueGenerateLibraryVideoThumbnails(id, name, forceOverride: force);
        return Results.Accepted();
    }

    private static async Task<IResult> GetThumbnailsLockedAsync(Guid id, IMediaRepository repository)
    {
        var locked = await repository.AreThumbnailsLockedAsync(id);
        return Results.Ok(new ThumbnailsLockDto { Locked = locked });
    }

    private static async Task<IResult> SetThumbnailsLockedAsync(Guid id, [FromBody] ThumbnailsLockDto body, IMediaRepository repository)
    {
        await repository.SetThumbnailsLockedAsync(id, body.Locked);
        return Results.Ok(new ThumbnailsLockDto { Locked = body.Locked });
    }

    private static async Task<IResult> GetLibraryCoverageAsync(Guid id, IVideoThumbnailManager manager)
    {
        var (total, withThumbnails) = await manager.GetCoverageAsync(id);
        return Results.Ok(new ThumbnailCoverageDto { Total = total, WithThumbnails = withThumbnails });
    }
}

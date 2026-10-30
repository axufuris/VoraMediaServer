using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Vora.Application.Artwork;
using Vora.Application.FileSystem;
using Vora.Application.Settings;
using Vora.Domain.Enums;

namespace Vora.Api.Endpoints;

public static partial class ArtworkEndpoints
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]+\.(png|jpg|jpeg|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex CustomArtworkFileNameRegex();

    public static RouteGroupBuilder MapArtworkEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api").WithTags("Artwork");

        group.MapGet("/artwork/custom/{fileName}", ServeCustomArtwork)
            .AllowAnonymous();

        group.MapGet("/artwork/local/{mediaItemId:guid}/{fileName}", ServeLocalArtworkAsync)
            .AllowAnonymous();

        group.MapGet("/artwork/thumb", ServeThumbnailAsync)
            .AllowAnonymous();

        group.MapGet("/artwork/vora/{category}/{fileName}", ServeVoraPoster)
            .AllowAnonymous();

        var authGroup = group.MapGroup("").RequireAuthorization();

        authGroup.MapGet("/artwork/vora", GetVoraPosters)
            .Produces<VoraPosterCatalogVM>(StatusCodes.Status200OK);

        authGroup.MapGet("/media/{id:guid}/artwork", GetMediaArtworkAsync)
            .Produces<IEnumerable<MediaArtworkVM>>(StatusCodes.Status200OK);
        authGroup.MapPost("/media/{id:guid}/artwork/fetch", RefreshMediaArtworkAsync).RequireAuthorization("AdminOnly");
        authGroup.MapPost("/media/{id:guid}/artwork/upload", UploadMediaArtworkAsync).RequireAuthorization("AdminOnly").DisableAntiforgery()
            .Produces<UploadedImageResponse>(StatusCodes.Status200OK);
        authGroup.MapPost("/media/{id:guid}/artwork/url", AddMediaArtworkUrlAsync).RequireAuthorization("AdminOnly");
        authGroup.MapDelete("/media/artwork/{artworkId:guid}", DeleteMediaArtworkAsync).RequireAuthorization("AdminOnly");

        return group;
    }

    private static IResult ServeCustomArtwork(string fileName, IOptions<StoragePathsOptions> storagePaths, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !CustomArtworkFileNameRegex().IsMatch(fileName))
        {
            return Results.NotFound();
        }

        var basePath = StorageRoots.CustomArtwork(storagePaths.Value);

        var path = SafePathResolver.ResolveContainedFilePath(basePath, fileName);
        if (path == null || !File.Exists(path))
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        return Results.File(path, ImageContentType(path));
    }

    private static async Task<IResult> ServeLocalArtworkAsync(Guid mediaItemId, string fileName, ILocalArtworkLocator localArtwork, HttpContext httpContext)
    {
        var path = await localArtwork.ResolveAsync(mediaItemId, fileName);
        if (path == null || !File.Exists(path))
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        return Results.File(path, ImageContentType(path));
    }

    private static IResult ServeVoraPoster(string category, string fileName, IVoraPosterCatalog posters, HttpContext httpContext)
    {
        var poster = posters.Find(category, fileName);
        if (poster == null || !File.Exists(poster.FilePath))
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        return Results.File(poster.FilePath, "image/jpeg");
    }

    private static IResult GetVoraPosters(IVoraPosterCatalog posters) =>
        Results.Ok(VoraPosterCatalogVM.From(posters));

    private static string ImageContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static async Task<IResult> ServeThumbnailAsync(
        [FromQuery] string? src,
        [FromQuery] int? w,
        [FromQuery] string? kind,
        IArtworkThumbnailService thumbnails,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return Results.NotFound();
        }

        var width = ClampThumbnailWidth(w ?? 360);
        var path = await thumbnails.GetOrCreateThumbnailAsync(src, width, NormalizeKind(kind), cancellationToken);
        if (path == null || !File.Exists(path))
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        return Results.File(path, ThumbnailContentType(path));
    }

    private static string NormalizeKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "still" => "still",
        "backdrop" => "backdrop",
        "logo" => "logo",
        _ => "poster"
    };

    // A logo keeps its transparency, so it is cached as a PNG; everything else
    // is re-encoded to JPEG. The extension the cache chose is the answer.
    private static string ThumbnailContentType(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

    private static int ClampThumbnailWidth(int w) => w switch
    {
        <= 200 => 200,
        <= 360 => 360,
        <= 500 => 500,
        <= 780 => 780,
        <= 1280 => 1280,
        _ => 1920
    };

    private static async Task<IResult> GetMediaArtworkAsync(Guid id, IArtworkService service)
    {
        var artwork = await service.GetArtworkOptionsAsync(id);
        return Results.Ok(artwork);
    }

    private static async Task<IResult> RefreshMediaArtworkAsync(Guid id, [FromQuery] string? providerId, IArtworkService service)
    {
        await service.RefreshProviderArtworkAsync(id, providerId);
        return Results.Accepted();
    }

    private static async Task<IResult> UploadMediaArtworkAsync(Guid id, IFormFile file, [FromQuery] ArtworkKind kind, IArtworkService service)
    {
        await using var stream = file.OpenReadStream();
        var url = await service.UploadAsync(id, new UploadedFile(stream, file.FileName, file.ContentType), kind);
        return Results.Ok(new UploadedImageResponse { Url = url });
    }

    private static async Task<IResult> AddMediaArtworkUrlAsync(Guid id, [FromBody] string url, [FromQuery] ArtworkKind kind, IArtworkService service)
    {
        await service.AddUrlAsync(id, url, kind);
        return Results.Ok();
    }

    private static async Task<IResult> DeleteMediaArtworkAsync(Guid artworkId, IArtworkService service)
    {
        await service.DeleteAsync(artworkId);
        return Results.NoContent();
    }
}

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

        var authGroup = group.MapGroup("").RequireAuthorization();

        authGroup.MapGet("/media/{id:guid}/artwork", GetMediaArtworkAsync);
        authGroup.MapPost("/media/{id:guid}/artwork/fetch", RefreshMediaArtworkAsync).RequireAuthorization("AdminOnly");
        authGroup.MapPost("/media/{id:guid}/artwork/upload", UploadMediaArtworkAsync).RequireAuthorization("AdminOnly").DisableAntiforgery();
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

        var configPath = storagePaths.Value.CustomArtwork;
        var basePath = !string.IsNullOrWhiteSpace(configPath)
            ? configPath
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        var path = SafePathResolver.ResolveContainedFilePath(basePath, fileName);
        if (path == null || !File.Exists(path))
        {
            return Results.NotFound();
        }

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        httpContext.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        return Results.File(path, contentType);
    }

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
        return Results.Ok(new { Url = url });
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

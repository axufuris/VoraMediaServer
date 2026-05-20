using Microsoft.AspNetCore.Mvc;
using Vora.Application.Artwork;
using Vora.Domain.Enums;

namespace Vora.Api.Endpoints;

public static class ArtworkEndpoints
{
    public static RouteGroupBuilder MapArtworkEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api").WithTags("Artwork");

        group.MapGet("/artwork/custom/{fileName}", ServeCustomArtwork)
            .AllowAnonymous();

        var authGroup = group.MapGroup("").RequireAuthorization();

        authGroup.MapGet("/media/{id:guid}/artwork", GetMediaArtworkAsync);
        authGroup.MapPost("/media/{id:guid}/artwork/upload", UploadMediaArtworkAsync).DisableAntiforgery();
        authGroup.MapPost("/media/{id:guid}/artwork/url", AddMediaArtworkUrlAsync);
        authGroup.MapDelete("/media/artwork/{artworkId:guid}", DeleteMediaArtworkAsync);

        return group;
    }

    private static IResult ServeCustomArtwork(string fileName, IConfiguration config)
    {
        var configPath = config["StoragePaths:CustomArtwork"];
        var basePath = !string.IsNullOrWhiteSpace(configPath)
            ? configPath
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        var path = Path.Combine(basePath, fileName);
        if (!File.Exists(path)) return Results.NotFound();

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
        return Results.File(path, contentType);
    }

    private static async Task<IResult> GetMediaArtworkAsync(Guid id, IArtworkService service)
    {
        var artwork = await service.GetArtworkOptionsAsync(id);
        return Results.Ok(artwork);
    }

    private static async Task<IResult> UploadMediaArtworkAsync(Guid id, [FromForm] IFormFile file, [FromQuery] ArtworkKind kind, IArtworkService service)
    {
        var url = await service.UploadAsync(id, file, kind);
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

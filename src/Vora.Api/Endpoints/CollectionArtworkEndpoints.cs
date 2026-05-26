using Microsoft.AspNetCore.Mvc;
using Vora.Application.Collections;
using Vora.Application.FileSystem;
using Vora.Domain.Enums;

namespace Vora.Api.Endpoints;

public static class CollectionArtworkEndpoints
{
    public static RouteGroupBuilder MapCollectionArtworkEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/collections").WithTags("Collection Artwork").RequireAuthorization();

        group.MapGet("/{id:guid}/artwork", GetArtworkAsync)
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/artwork/upload", UploadArtworkAsync)
            .DisableAntiforgery();

        group.MapPost("/{id:guid}/artwork/url", AddArtworkUrlAsync);

        group.MapPost("/{id:guid}/artwork/fetch", RefreshProviderArtworkAsync);

        group.MapDelete("/artwork/{artworkId:guid}", DeleteArtworkAsync);

        return group;
    }

    private static async Task<IResult> GetArtworkAsync(Guid id, ICollectionArtworkService service)
    {
        var artwork = await service.GetArtworkAsync(id);
        var dto = artwork.Select(a => new
        {
            Id = a.Id.ToString(),
            a.IsUserUploaded,
            a.Url,
            Kind = a.Kind.ToString()
        });
        return Results.Ok(dto);
    }

    private static async Task<IResult> UploadArtworkAsync(Guid id, [FromForm] IFormFile file, [FromQuery] ArtworkKind kind, ICollectionArtworkService service)
    {
        await using var stream = file.OpenReadStream();
        var url = await service.UploadAsync(id, new UploadedFile(stream, file.FileName, file.ContentType), kind);
        return Results.Ok(new { Url = url });
    }

    private static async Task<IResult> AddArtworkUrlAsync(Guid id, [FromBody] string url, [FromQuery] ArtworkKind kind, ICollectionArtworkService service)
    {
        await service.AddUrlAsync(id, url, kind);
        return Results.Ok();
    }

    private static async Task<IResult> RefreshProviderArtworkAsync(Guid id, [FromQuery] string providerId, ICollectionArtworkService service)
    {
        await service.RefreshProviderArtworkAsync(id, providerId);
        return Results.Accepted();
    }

    private static async Task<IResult> DeleteArtworkAsync(Guid artworkId, ICollectionArtworkService service)
    {
        await service.DeleteAsync(artworkId);
        return Results.NoContent();
    }
}

using Microsoft.AspNetCore.Mvc;
using Vora.Application.Subtitles;
using Vora.Application.Subtitles.ViewModels;

namespace Vora.Api.Endpoints;

public static class SubtitleSearchEndpoints
{
    public static RouteGroupBuilder MapSubtitleSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/media").WithTags("Subtitle Search").RequireAuthorization();

        group.MapGet("/{id:guid}/subtitles/search", SearchAsync)
            .WithName("SearchSubtitles")
            .Produces<List<SubtitleSearchResultVM>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/subtitles/download", DownloadAsync)
            .WithName("DownloadSubtitle")
            .Produces<DownloadedSubtitleVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    // 404 rather than an empty list when no provider is configured: the clients
    // hide the feature on the availability flag, so a request arriving here at
    // all means something is out of step, and an empty list would read as "this
    // title has no subtitles" instead.
    private static async Task<IResult> SearchAsync(
        Guid id,
        [FromQuery] string? languages,
        ISubtitleSearchManager manager,
        CancellationToken ct)
    {
        if (!await manager.IsAvailableAsync(ct)) return Results.NotFound();

        var requested = ParseLanguages(languages);
        var results = await manager.SearchAsync(id, requested, ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        [FromBody] DownloadSubtitleRequest request,
        ISubtitleSearchManager manager,
        CancellationToken ct)
    {
        if (!await manager.IsAvailableAsync(ct)) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.ProviderFileId)) return Results.BadRequest("A providerFileId is required.");

        var track = await manager.DownloadAndAttachAsync(id, request.ProviderFileId, request.Language, ct);
        return track == null ? Results.NotFound() : Results.Ok(track);
    }

    public static List<string> ParseLanguages(string? languages) =>
        (languages ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.ToLowerInvariant())
            .Distinct()
            .ToList();
}

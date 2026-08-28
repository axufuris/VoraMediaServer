using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Requests;
using Vora.Application.Watchlist;
using Vora.Application.Watchlist.ViewModels;

namespace Vora.Api.Endpoints;

public class ToggleWatchlistRequest
{
    public Guid? MediaItemId { get; set; }
    public string? ExternalId { get; set; }
    public string? ProviderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public DateTime? ExpectedReleaseDate { get; set; }
}

// Deliberately outside the /api/discovery group: that group is gated on the
// Discover feature, and the watchlist holds library items too, so it has to
// work on a server with Discover switched off.
public static class WatchlistEndpoints
{
    public static IEndpointRouteBuilder MapWatchlistEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/watchlist").WithTags("Watchlist").RequireAuthorization();

        group.MapGet("/", GetWatchlistAsync)
            .WithName("ListWatchlist")
            .Produces<IEnumerable<WatchlistItemVM>>(StatusCodes.Status200OK);

        group.MapGet("/check", CheckWatchlistAsync)
            .WithName("CheckWatchlist")
            .Produces<WatchlistStatusVM>(StatusCodes.Status200OK);

        group.MapPost("/toggle", ToggleWatchlistAsync)
            .WithName("ToggleWatchlist")
            .Produces<WatchlistStatusVM>(StatusCodes.Status200OK);

        return routes;
    }

    private static async Task<IResult> GetWatchlistAsync(ClaimsPrincipal user, IWatchlistManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();

        return Results.Ok(await manager.GetWatchlistAsync(profileId.Value));
    }

    private static async Task<IResult> CheckWatchlistAsync(
        ClaimsPrincipal user,
        IWatchlistManager manager,
        [FromQuery] string? externalId,
        [FromQuery] string? providerId,
        [FromQuery] Guid? mediaItemId)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();

        var inWatchlist = await manager.IsInWatchlistAsync(profileId.Value, externalId, providerId, mediaItemId);
        return Results.Ok(new WatchlistStatusVM { InWatchlist = inWatchlist });
    }

    private static async Task<IResult> ToggleWatchlistAsync(
        ClaimsPrincipal user,
        [FromBody] ToggleWatchlistRequest request,
        IWatchlistManager manager,
        IRequestManager requestManager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();

        var added = await manager.ToggleAsync(profileId.Value, new WatchlistRequest
        {
            MediaItemId = request.MediaItemId,
            ExternalId = request.ExternalId,
            ProviderId = request.ProviderId,
            Type = request.Type,
            Title = request.Title,
            PosterUrl = request.PosterUrl,
            ExpectedReleaseDate = request.ExpectedReleaseDate,
        });

        // Auto-request only applies to titles that aren't in the library.
        if (added && request.MediaItemId == null && !string.IsNullOrWhiteSpace(request.ExternalId))
        {
            await requestManager.ProcessWatchlistAdditionAsync(
                request.ExternalId,
                request.ProviderId ?? string.Empty,
                request.Title,
                request.Type,
                request.PosterUrl ?? string.Empty,
                profileId.Value,
                request.ExpectedReleaseDate);
        }

        return Results.Ok(new WatchlistStatusVM { InWatchlist = added });
    }
}

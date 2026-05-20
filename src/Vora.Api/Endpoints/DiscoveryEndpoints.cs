using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Discovery;
using Vora.Application.Requests;
using Vora.Domain.Entities.Discovery;
using Vora.Plugins.Dtos;

namespace Vora.Api.Endpoints;

public class ToggleWatchlistRequest
{
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public DateTime? ExpectedReleaseDate { get; set; }
}

public static class DiscoveryEndpoints
{
    public static RouteGroupBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/discovery").WithTags("Discovery").RequireAuthorization().RequireFeature(FeatureGate.Discover);

        group.MapGet("/config", GetAdminConfigsAsync);
        group.MapPut("/config", UpdateAdminConfigsAsync).RequireAuthorization("AdminOnly");

        group.MapGet("/rows/{providerId}/{rowId}/items", GetRowItemsAsync);
        group.MapGet("/details/{providerId}/{type}/{externalId}", GetItemDetailsAsync);
        group.MapGet("/actor/{providerId}/{externalId}", GetActorAsync);
        group.MapGet("/search", SearchAsync);

        group.MapGet("/profiles/{profileId:guid}/watchlist", GetWatchlistAsync);
        group.MapGet("/profiles/{profileId:guid}/watchlist/check", CheckWatchlistAsync);
        group.MapPost("/profiles/{profileId:guid}/watchlist/toggle", ToggleWatchlistAsync);

        group.MapGet("/theater/showtimes", GetShowtimesAsync);
        group.MapGet("/theater/auto-load", IsTheaterAutoLoadEnabledAsync);

        return group;
    }

    private static async Task<IResult> GetAdminConfigsAsync(IDiscoveryManager manager) =>
        Results.Ok(await manager.GetAdminRowConfigsAsync());

    private static async Task<IResult> UpdateAdminConfigsAsync([FromBody] List<DiscoveryRowConfig> configs, IDiscoveryManager manager)
    {
        await manager.UpdateAdminRowConfigsAsync(configs);
        return Results.NoContent();
    }

    private static async Task<IResult> GetRowItemsAsync(string providerId, string rowId, [FromQuery] int page, IDiscoveryManager manager)
    {
        try
        {
            return Results.Ok(await manager.GetRowItemsAsync(providerId, rowId, page > 0 ? page : 1));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Discovery provider error");
        }
    }

    private static async Task<IResult> GetItemDetailsAsync(string providerId, string type, string externalId, IDiscoveryManager manager)
    {
        var details = await manager.GetItemDetailsAsync(providerId, externalId, type);
        return details != null ? Results.Ok(details) : Results.NotFound();
    }

    private static async Task<IResult> GetActorAsync(string providerId, string externalId, IDiscoveryManager manager)
    {
        var actor = await manager.GetActorDetailsAsync(providerId, externalId);
        return actor != null ? Results.Ok(actor) : Results.NotFound();
    }

    private static async Task<IResult> SearchAsync([FromQuery] string q, IDiscoveryManager manager)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 3)
        {
            return Results.Ok(new List<DiscoveryItemDto>());
        }
        return Results.Ok(await manager.SearchAsync(q));
    }

    private static async Task<IResult> GetWatchlistAsync(Guid profileId, IDiscoveryManager manager) =>
        Results.Ok(await manager.GetWatchlistAsync(profileId));

    private static async Task<IResult> CheckWatchlistAsync(
        Guid profileId,
        [FromQuery] string externalId,
        [FromQuery] string providerId,
        IDiscoveryManager manager) =>
        Results.Ok(new { inWatchlist = await manager.CheckWatchlistStatusAsync(profileId, externalId, providerId) });

    private static async Task<IResult> ToggleWatchlistAsync(
        Guid profileId,
        [FromBody] ToggleWatchlistRequest req,
        IDiscoveryManager manager,
        IRequestManager requestManager)
    {
        await manager.ToggleWatchlistAsync(profileId, req.ExternalId, req.ProviderId, req.Type, req.Title, req.PosterUrl, req.ExpectedReleaseDate);

        var inWatchlist = await manager.CheckWatchlistStatusAsync(profileId, req.ExternalId, req.ProviderId);
        if (inWatchlist)
        {
            await requestManager.ProcessWatchlistAdditionAsync(req.ExternalId, req.ProviderId, req.Title, req.Type, req.PosterUrl ?? string.Empty, profileId, req.ExpectedReleaseDate);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetShowtimesAsync(
        [FromQuery] string movieTitle,
        [FromQuery] string location,
        [FromQuery] int? maxTheaters,
        IDiscoveryManager manager)
    {
        if (string.IsNullOrWhiteSpace(movieTitle))
        {
            return Results.BadRequest("Movie title is required");
        }

        var showtimes = await manager.GetShowtimesAsync(movieTitle, location, DateTime.UtcNow, maxTheaters);
        return Results.Ok(showtimes);
    }

    private static async Task<IResult> IsTheaterAutoLoadEnabledAsync(IDiscoveryManager manager)
    {
        var autoLoad = await manager.IsTheaterAutoLoadEnabledAsync();
        return Results.Ok(autoLoad);
    }
}

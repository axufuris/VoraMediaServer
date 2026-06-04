using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Podcasts;
using Vora.Application.Podcasts.ViewModels;

namespace Vora.Api.Endpoints;

public class SubscribeToPodcastDto
{
    public required string FeedUrl { get; set; }
}

public class SaveEpisodeStateDto
{
    public double PositionSeconds { get; set; }
    public bool? IsPlayed { get; set; }
}

public static class PodcastEndpoints
{
    public static IEndpointRouteBuilder MapPodcastEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/podcasts").WithTags("Podcasts").RequireAuthorization().RequireFeature(FeatureGate.Podcasts);

        group.MapGet("/subscriptions", GetSubscriptionsAsync)
            .WithName("ListPodcastSubscriptions")
            .Produces<IEnumerable<PodcastSubscriptionVM>>(StatusCodes.Status200OK);
        group.MapPost("/subscriptions", SubscribeAsync)
            .WithName("SubscribeToPodcast")
            .Produces<PodcastSubscriptionVM>(StatusCodes.Status200OK);
        group.MapDelete("/subscriptions/{subscriptionId:guid}", UnsubscribeAsync)
            .WithName("UnsubscribeFromPodcast")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/subscriptions/{subscriptionId:guid}/refresh", RefreshSubscriptionAsync)
            .WithName("RefreshPodcastSubscription")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/subscriptions/{subscriptionId:guid}/episodes", GetEpisodesAsync)
            .WithName("ListSubscriptionEpisodes")
            .Produces<IEnumerable<PodcastEpisodeVM>>(StatusCodes.Status200OK);
        group.MapPost("/episodes/{episodeId:guid}/state", SaveEpisodeStateAsync)
            .WithName("SavePodcastEpisodeState");
        group.MapGet("/episodes/recent", GetRecentEpisodesAsync)
            .WithName("ListRecentPodcastEpisodes")
            .Produces<IEnumerable<PodcastFeedEpisodeVM>>(StatusCodes.Status200OK);
        group.MapGet("/search", SearchAsync)
            .WithName("SearchPodcasts")
            .Produces<IEnumerable<DiscoveredPodcastVM>>(StatusCodes.Status200OK);

        group.MapGet("/catalog", GetCatalogAsync)
            .WithName("GetPodcastCatalog")
            .Produces<IEnumerable<CatalogPodcastVM>>(StatusCodes.Status200OK);
        group.MapPost("/admin/catalog", AddCatalogPodcastAsync);
        group.MapDelete("/admin/catalog/{showId:guid}", RemoveCatalogPodcastAsync);

        return routes;
    }

    private static async Task<IResult> GetCatalogAsync(ClaimsPrincipal user, IPodcastManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        var catalog = await manager.GetCatalogAsync(profileId.Value);
        return Results.Ok(catalog);
    }

    private static async Task<IResult> AddCatalogPodcastAsync([FromBody] SubscribeToPodcastDto request, ClaimsPrincipal user, IPodcastManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();

        try
        {
            var entry = await manager.AddToCatalogAsync(request.FeedUrl);
            return Results.Ok(entry);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to fetch or parse feed: {ex.Message}" });
        }
    }

    private static async Task<IResult> RemoveCatalogPodcastAsync(Guid showId, ClaimsPrincipal user, IPodcastManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        await manager.RemoveFromCatalogAsync(showId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetRecentEpisodesAsync(
        ClaimsPrincipal user,
        IPodcastManager manager,
        [FromQuery] int? limit,
        [FromQuery] int? days)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        var episodes = await manager.GetRecentEpisodesAsync(profileId.Value, limit ?? 50, days);
        return Results.Ok(episodes);
    }

    private static async Task<IResult> SearchAsync(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        ClaimsPrincipal user,
        IPodcastManager manager,
        CancellationToken cancellationToken)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<object>());

        var results = await manager.SearchAsync(q, limit ?? 25, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> GetSubscriptionsAsync(ClaimsPrincipal user, IPodcastManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        var subs = await manager.GetSubscriptionsAsync(profileId.Value);
        return Results.Ok(subs);
    }

    private static async Task<IResult> SubscribeAsync([FromBody] SubscribeToPodcastDto request, ClaimsPrincipal user, IPodcastManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();

        var canAddCustomFeeds = user.CanAddCustomPodcastFeeds();

        try
        {
            var sub = await manager.SubscribeAsync(profileId.Value, request.FeedUrl, canAddCustomFeeds);
            return Results.Ok(sub);
        }
        catch (PodcastPermissionDeniedException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to fetch or parse feed: {ex.Message}" });
        }
    }

    private static async Task<IResult> UnsubscribeAsync(Guid subscriptionId, ClaimsPrincipal user, IPodcastManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        await manager.UnsubscribeAsync(profileId.Value, subscriptionId);
        return Results.NoContent();
    }

    private static async Task<IResult> RefreshSubscriptionAsync(Guid subscriptionId, ClaimsPrincipal user, IPodcastManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();

        try
        {
            await manager.RefreshSubscriptionAsync(profileId.Value, subscriptionId);
            return Results.NoContent();
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Refresh failed: {ex.Message}" });
        }
    }

    private static async Task<IResult> GetEpisodesAsync(Guid subscriptionId, ClaimsPrincipal user, IPodcastManager manager, [FromQuery] int? limit)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        var episodes = await manager.GetEpisodesAsync(profileId.Value, subscriptionId, limit ?? 100);
        return Results.Ok(episodes);
    }

    private static async Task<IResult> SaveEpisodeStateAsync(Guid episodeId, [FromBody] SaveEpisodeStateDto request, ClaimsPrincipal user, IPodcastManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Unauthorized();

        try
        {
            await manager.SaveEpisodeStateAsync(profileId.Value, episodeId, request.PositionSeconds, request.IsPlayed);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
    }
}

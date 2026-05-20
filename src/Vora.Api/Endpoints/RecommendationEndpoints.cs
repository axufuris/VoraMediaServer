using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Recommendations;
using Vora.Application.Recommendations.ViewModels;
using Vora.Application.Ai.ViewModels;
using Vora.Application.Tasks;

namespace Vora.Api.Endpoints;

public static class RecommendationEndpoints
{
    public static IEndpointRouteBuilder MapRecommendationEndpoints(this IEndpointRouteBuilder routes)
    {
        MapClientRecommendationEndpoints(routes);
        MapAdminRecommendationEndpoints(routes);
        return routes;
    }

    private static void MapClientRecommendationEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/recommendations").WithTags("Recommendations").RequireAuthorization().RequireFeature(FeatureGate.ForYou);

        group.MapGet("/providers", GetProvidersAsync)
            .Produces<List<string>>(StatusCodes.Status200OK);

        group.MapGet("/global", GetGlobalAsync)
            .Produces<List<RecommendationListVM>>(StatusCodes.Status200OK);

        var libraryGroup = routes.MapGroup("/api/libraries/{libraryId:guid}/recommendations")
            .WithTags("Recommendations")
            .RequireAuthorization()
            .RequireFeature(FeatureGate.ForYou);

        libraryGroup.MapGet("/", GetForLibraryAsync)
            .Produces<List<RecommendationListVM>>(StatusCodes.Status200OK);
    }

    private static void MapAdminRecommendationEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/ai-stats").WithTags("AI Stats (Admin)").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAiStatsAsync)
            .Produces<AiStatsDashboardVM>(StatusCodes.Status200OK);

        group.MapPost("/trigger", TriggerAiTaskAsync)
            .Produces(StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetProvidersAsync(IRecommendationManager manager)
    {
        var providers = await manager.GetActiveProviderIdsAsync();
        return Results.Ok(providers);
    }

    private static async Task<IResult> GetGlobalAsync(
        [FromQuery] string? providerId,
        ClaimsPrincipal user,
        IRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null)
        {
            return Results.Unauthorized();
        }

        var lists = await manager.GetRecommendationsAsync(
            profileId.Value,
            null,
            user.HasAllLibraryAccess(),
            user.GetAllowedLibraryIds(),
            user.HasAllContentRatings(),
            user.GetAllowedMovieRatings(),
            user.GetAllowedTvRatings(),
            user.BlockUnratedContent(),
            providerId);

        return Results.Ok(lists);
    }

    private static async Task<IResult> GetForLibraryAsync(
        Guid libraryId,
        [FromQuery] string? providerId,
        ClaimsPrincipal user,
        IRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null)
        {
            return Results.Unauthorized();
        }

        if (!user.HasAllLibraryAccess() && !user.GetAllowedLibraryIds().Contains(libraryId))
        {
            return Results.Forbid();
        }

        var lists = await manager.GetRecommendationsAsync(
            profileId.Value,
            libraryId,
            user.HasAllLibraryAccess(),
            user.GetAllowedLibraryIds(),
            user.HasAllContentRatings(),
            user.GetAllowedMovieRatings(),
            user.GetAllowedTvRatings(),
            user.BlockUnratedContent(),
            providerId);

        return Results.Ok(lists);
    }

    private static async Task<IResult> GetAiStatsAsync(
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize,
        IOpenAiRecommendationRepository repo)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 50 : pageSize;

        var dashboard = await repo.GetAiStatsDashboardAsync(startDate, endDate, page, pageSize);
        return Results.Ok(dashboard);
    }

    private static IResult TriggerAiTaskAsync(ITaskQueueManager taskQueue)
    {
        taskQueue.QueueGenerateAiEmbeddings();
        return Results.Ok();
    }
}

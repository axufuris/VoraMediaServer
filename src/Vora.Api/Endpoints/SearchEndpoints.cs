using System.Security.Claims;
using Vora.Api.Extensions;
using Vora.Application.Search;
using Vora.Application.Search.ViewModels;

namespace Vora.Api.Endpoints;

public static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/search").WithTags("Global Search").RequireAuthorization();

        group.MapGet("/", SearchAllAsync)
            .Produces<GlobalSearchVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> SearchAllAsync(string? q, ClaimsPrincipal user, ISearchManager manager)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
        {
            return Results.BadRequest(new { Message = "Search query must be at least 3 characters." });
        }

        var results = await manager.SearchAllAsync(
            q.Trim(),
            user.HasAllLibraryAccess(),
            user.GetAllowedLibraryIds(),
            user.HasAllContentRatings(),
            user.GetAllowedMovieRatings(),
            user.GetAllowedTvRatings(),
            user.GetAllowedMusicRatings(),
            user.BlockUnratedContent());

        return Results.Ok(results);
    }
}

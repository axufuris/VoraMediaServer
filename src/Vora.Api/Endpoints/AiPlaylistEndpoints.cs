using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Media.Ai;
using Vora.Application.Tasks;

namespace Vora.Api.Endpoints;

// AI playlists for music. Kept apart from the ordinary mixes list, which older
// clients read and would not know these kinds in. A made playlist is a mix:
// the existing mix detail and "save" routes open and keep it.
public static class AiPlaylistEndpoints
{
    public static IEndpointRouteBuilder MapAiPlaylistEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/music/ai").WithTags("AI Playlists").RequireAuthorization();

        // Enabled is false when the server has AI playlists off or this profile
        // opted out; clients hide everything then.
        group.MapGet("/", GetAsync)
            .WithName("GetAiPlaylists")
            .Produces<AiPlaylistsVM>(StatusCodes.Status200OK);

        group.MapPost("/requests", MakeAsync)
            .WithName("MakeAiPlaylist")
            .Produces<AiPlaylistCreatedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapGet("/blend-partners", GetPartnersAsync)
            .WithName("ListBlendPartners")
            .Produces<List<BlendPartner>>(StatusCodes.Status200OK);

        group.MapPost("/blends", BlendAsync)
            .WithName("CreateBlend")
            .Produces<AiPlaylistCreatedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Admin: make this week's set for every eligible profile now, instead of
        // waiting for the nightly run to find them due.
        group.MapPost("/generate", Generate)
            .RequireAuthorization("AdminOnly")
            .WithName("GenerateAiPlaylists")
            .Produces(StatusCodes.Status202Accepted);

        return routes;
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal user, IAiPlaylistService service)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        return Results.Ok(await service.GetForProfileAsync(profileId.Value));
    }

    private static async Task<IResult> MakeAsync([FromBody] MakeAiPlaylistRequest request, ClaimsPrincipal user, IAiPlaylistService service, CancellationToken ct)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        return ToResult(await service.CreateFromRequestAsync(profileId.Value, request.Prompt, user.GetMusicAccessFilter(), ct));
    }

    private static async Task<IResult> GetPartnersAsync(ClaimsPrincipal user, IAiPlaylistService service)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        return Results.Ok(await service.GetBlendPartnersAsync(profileId.Value));
    }

    private static async Task<IResult> BlendAsync([FromBody] CreateBlendRequest request, ClaimsPrincipal user, IAiPlaylistService service, CancellationToken ct)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        return ToResult(await service.CreateBlendAsync(profileId.Value, request.PartnerProfileId, user.GetMusicAccessFilter(), ct));
    }

    private static IResult Generate(ITaskQueueManager tasks)
    {
        tasks.QueueGenerateAiPlaylists(force: true);
        return Results.Accepted();
    }

    private static IResult ToResult(AiResult result) => result.Outcome switch
    {
        AiOutcome.Made when result.MixId is Guid id => Results.Ok(new AiPlaylistCreatedResponse { MixId = id }),
        AiOutcome.Invalid => Results.Problem(detail: result.Message, statusCode: StatusCodes.Status400BadRequest),
        AiOutcome.Unavailable => Results.Problem(detail: result.Message, statusCode: StatusCodes.Status403Forbidden),
        AiOutcome.LimitReached => Results.Problem(detail: result.Message, statusCode: StatusCodes.Status429TooManyRequests),
        AiOutcome.PartnerUnavailable or AiOutcome.NothingFound => Results.Problem(detail: result.Message, statusCode: StatusCodes.Status404NotFound),
        AiOutcome.NotEnoughListening => Results.Problem(detail: result.Message, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(detail: result.Message, statusCode: StatusCodes.Status502BadGateway),
    };
}

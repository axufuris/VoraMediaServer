using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Iptv;

namespace Vora.Api.Endpoints;

public class StartTimeshiftRequestDto
{
    public Guid ChannelId { get; set; }
}

public static class TimeshiftEndpoints
{
    public static RouteGroupBuilder MapTimeshiftEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/iptv/timeshift").WithTags("Timeshift").RequireAuthorization().RequireFeature(FeatureGate.LiveTv);

        group.MapPost("/start", StartAsync);
        group.MapPost("/stop", StopAsync);
        group.MapPost("/ping", PingAsync);

        return group;
    }

    private static async Task<IResult> StartAsync(
        [FromBody] StartTimeshiftRequestDto request,
        ClaimsPrincipal user,
        IIptvManager manager)
    {
        var accountId = user.GetAccountId();
        var profileId = user.GetProfileId();

        if (accountId == null || profileId == null)
        {
            return Results.Unauthorized();
        }

        if (!user.CanTimeshiftIptv())
        {
            return Results.Forbid();
        }

        var url = await manager.StartTimeshiftSessionAsync(request.ChannelId, accountId.Value, profileId.Value);
        if (url == null)
        {
            return Results.StatusCode(StatusCodes.Status409Conflict);
        }
        return Results.Ok(new { url });
    }

    private static async Task<IResult> StopAsync(ClaimsPrincipal user, IIptvManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId != null)
        {
            await manager.StopTimeshiftSessionAsync(profileId.Value);
        }
        return Results.Ok();
    }

    private static IResult PingAsync(ClaimsPrincipal user, IIptvManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId != null)
        {
            manager.PingTimeshiftSession(profileId.Value);
        }
        return Results.Ok();
    }
}

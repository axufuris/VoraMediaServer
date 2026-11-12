using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Iptv;
using Vora.Application.Iptv.Dtos;
using Vora.Application.Settings;

namespace Vora.Api.Endpoints;

public class GuideRequestDto
{
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public List<string> ChannelIds { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

public static class IptvClientEndpoints
{
    public static RouteGroupBuilder MapIptvClientEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/iptv").WithTags("IPTV (Client)").RequireAuthorization();

        group.MapGet("/client/playlists/{userId:guid}", GetClientPlaylistsAsync)
            .WithName("ListClientIptvPlaylists")
            .Produces<IEnumerable<Vora.Application.Iptv.ViewModels.IptvPlaylistVM>>(StatusCodes.Status200OK);
        group.MapPost("/guide", GetGuideAsync)
            .RequireFeature(FeatureGate.LiveTv)
            .WithName("GetIptvGuide")
            .Produces<Dictionary<string, List<IptvProgramDto>>>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task<IResult> GetClientPlaylistsAsync(Guid userId, [FromQuery] Guid? profileId, ClaimsPrincipal user, IIptvManager manager, ISystemSettingsRepository settingsRepo)
    {
        if (Viewer(user, userId, profileId) is not { } viewer) return Results.Unauthorized();

        var playlists = await manager.GetClientPlaylistsAsync(viewer.AccountId, viewer.ProfileId);

        if (!user.IsAdmin())
        {
            var settings = await settingsRepo.GetSettingsAsync();
            if (!settings.EnableLiveTv && !settings.EnableInternetRadio)
            {
                return Results.Ok(new List<Vora.Application.Iptv.ViewModels.IptvPlaylistVM>());
            }
            if (!settings.EnableLiveTv)
            {
                playlists = playlists.Where(p => string.Equals(p.DefaultChannelKind, "Radio", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (!settings.EnableInternetRadio)
            {
                playlists = playlists.Where(p => string.Equals(p.DefaultChannelKind, "Tv", StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        return Results.Ok(playlists);
    }

    private static async Task<IResult> GetGuideAsync([FromBody] GuideRequestDto request, ClaimsPrincipal user, IIptvManager manager)
    {
        if (Viewer(user, request.UserId, request.ProfileId) is not { } viewer) return Results.Unauthorized();

        var guide = await manager.GetFilteredGuideAsync(viewer.AccountId, viewer.ProfileId ?? Guid.Empty, request.ChannelIds, request.StartTime, request.EndTime);
        return Results.Ok(guide);
    }

    private static (Guid AccountId, Guid? ProfileId)? Viewer(ClaimsPrincipal user, Guid requestedAccountId, Guid? requestedProfileId)
    {
        if (user.GetAccountId() is not { } accountId) return null;
        return user.IsAdmin() ? (requestedAccountId, requestedProfileId) : (accountId, user.GetProfileId());
    }
}

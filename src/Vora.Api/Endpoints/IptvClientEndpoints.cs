using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Iptv;
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

        group.MapGet("/client/playlists/{userId:guid}", GetClientPlaylistsAsync);
        group.MapPost("/guide", GetGuideAsync).RequireFeature(FeatureGate.LiveTv);

        return group;
    }

    private static async Task<IResult> GetClientPlaylistsAsync(Guid userId, [FromQuery] Guid? profileId, ClaimsPrincipal user, IIptvManager manager, ISystemSettingsRepository settingsRepo)
    {
        var playlists = await manager.GetClientPlaylistsAsync(userId, profileId);

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

    private static async Task<IResult> GetGuideAsync([FromBody] GuideRequestDto request, IIptvManager manager)
    {
        var guide = await manager.GetFilteredGuideAsync(request.UserId, request.ProfileId, request.ChannelIds, request.StartTime, request.EndTime);
        return Results.Ok(guide);
    }
}

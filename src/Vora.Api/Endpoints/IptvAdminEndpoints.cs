using Microsoft.AspNetCore.Mvc;
using Vora.Application.Iptv;
using Vora.Domain.Enums;

namespace Vora.Api.Endpoints;

public class SetChannelKindDto
{
    public string Kind { get; set; } = "Tv";
}

public class CreateIptvPlaylistDto
{
    public required string Name { get; set; }
    public required string M3uUrl { get; set; }
    public bool SupportsWebPlayback { get; set; }
    public int MaxConcurrentStreams { get; set; }
    public string DefaultChannelKind { get; set; } = "Tv";
}

public class UpdateIptvPlaylistDto
{
    public required string Name { get; set; }
    public required string M3uUrl { get; set; }
    public bool SupportsWebPlayback { get; set; }
    public int MaxConcurrentStreams { get; set; }
    public bool IsActive { get; set; }
    public string DefaultChannelKind { get; set; } = "Tv";
}

public class CreateIptvEpgSourceDto
{
    public required string Name { get; set; }
    public required string XmlTvUrl { get; set; }
    public int Priority { get; set; }
}

public class UpdateIptvEpgSourceDto
{
    public required string Name { get; set; }
    public required string XmlTvUrl { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
}

public static class IptvAdminEndpoints
{
    public static RouteGroupBuilder MapIptvAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/iptv/admin").WithTags("IPTV (Admin)").RequireAuthorization("AdminOnly");

        group.MapGet("/playlists", GetAllPlaylistsAsync);
        group.MapPost("/playlists", AddPlaylistAsync);
        group.MapPut("/playlists/{id:guid}", UpdatePlaylistAsync);
        group.MapPost("/playlists/{id:guid}/refresh", RefreshPlaylistAsync);
        group.MapDelete("/playlists/{id:guid}", DeletePlaylistAsync);

        group.MapGet("/epg-sources", GetAllEpgSourcesAsync);
        group.MapPost("/epg-sources", AddEpgSourceAsync);
        group.MapPut("/epg-sources/{id:guid}", UpdateEpgSourceAsync);
        group.MapPost("/epg-sources/{id:guid}/refresh", RefreshEpgSourceAsync);
        group.MapDelete("/epg-sources/{id:guid}", DeleteEpgSourceAsync);
        group.MapGet("/epg-diagnostics", GetEpgDiagnosticsAsync);

        group.MapPut("/channels/{id:guid}/toggle-visibility", ToggleChannelVisibilityAsync);
        group.MapPut("/channels/{id:guid}/kind", SetChannelKindAsync);

        return group;
    }

    private static async Task<IResult> GetAllPlaylistsAsync([FromQuery] Vora.Domain.Enums.IptvChannelKind? kind, IIptvManager manager)
    {
        var playlists = await manager.GetAllPlaylistsAsync(kind);
        return Results.Ok(playlists);
    }

    private static async Task<IResult> AddPlaylistAsync([FromBody] CreateIptvPlaylistDto request, IIptvManager manager)
    {
        if (!Enum.TryParse<IptvChannelKind>(request.DefaultChannelKind, ignoreCase: true, out var defaultKind))
        {
            return Results.BadRequest($"Unknown default channel kind: {request.DefaultChannelKind}");
        }
        var playlist = await manager.AddPlaylistAsync(
            request.Name,
            request.M3uUrl,
            request.SupportsWebPlayback,
            request.MaxConcurrentStreams,
            defaultKind);
        return Results.Ok(playlist);
    }

    private static async Task<IResult> UpdatePlaylistAsync(Guid id, [FromBody] UpdateIptvPlaylistDto request, IIptvManager manager)
    {
        if (!Enum.TryParse<IptvChannelKind>(request.DefaultChannelKind, ignoreCase: true, out var defaultKind))
        {
            return Results.BadRequest($"Unknown default channel kind: {request.DefaultChannelKind}");
        }
        var playlist = await manager.UpdatePlaylistAsync(
            id,
            request.Name,
            request.M3uUrl,
            request.SupportsWebPlayback,
            request.MaxConcurrentStreams,
            request.IsActive,
            defaultKind);
        return Results.Ok(playlist);
    }

    private static async Task<IResult> RefreshPlaylistAsync(Guid id, IIptvManager manager)
    {
        await manager.RefreshPlaylistAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> DeletePlaylistAsync(Guid id, IIptvManager manager)
    {
        await manager.DeletePlaylistAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> GetAllEpgSourcesAsync(IIptvManager manager)
    {
        var sources = await manager.GetAllEpgSourcesAsync();
        return Results.Ok(sources);
    }

    private static async Task<IResult> AddEpgSourceAsync([FromBody] CreateIptvEpgSourceDto request, IIptvManager manager)
    {
        var source = await manager.AddEpgSourceAsync(request.Name, request.XmlTvUrl, request.Priority);
        return Results.Ok(source);
    }

    private static async Task<IResult> UpdateEpgSourceAsync(Guid id, [FromBody] UpdateIptvEpgSourceDto request, IIptvManager manager)
    {
        var source = await manager.UpdateEpgSourceAsync(id, request.Name, request.XmlTvUrl, request.Priority, request.IsActive);
        return Results.Ok(source);
    }

    private static async Task<IResult> RefreshEpgSourceAsync(Guid id, IIptvManager manager)
    {
        await manager.RefreshEpgSourceAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteEpgSourceAsync(Guid id, IIptvManager manager)
    {
        await manager.DeleteEpgSourceAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> GetEpgDiagnosticsAsync(IIptvManager manager)
    {
        var diagnostics = await manager.GetEpgDiagnosticsAsync();
        return Results.Ok(diagnostics);
    }

    private static async Task<IResult> ToggleChannelVisibilityAsync(Guid id, IIptvManager manager)
    {
        await manager.ToggleChannelVisibilityAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> SetChannelKindAsync(Guid id, [FromBody] SetChannelKindDto request, IIptvManager manager)
    {
        if (!Enum.TryParse<IptvChannelKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return Results.BadRequest($"Unknown channel kind: {request.Kind}");
        }
        await manager.SetChannelKindAsync(id, kind);
        return Results.NoContent();
    }
}

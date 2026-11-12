using Microsoft.AspNetCore.Mvc;
using Vora.Application.Iptv;
using Vora.Application.Iptv.Dtos;
using Vora.Domain.Enums;
using Vora.Application.Iptv.ViewModels;

namespace Vora.Api.Endpoints;

public class SetChannelKindDto
{
    public string Kind { get; set; } = "Tv";
}

public class CreateIptvPlaylistDto
{
    public required string Name { get; set; }
    public IptvSourceType? SourceType { get; set; }
    public string? M3uUrl { get; set; }
    public string? ServerUrl { get; set; }
    public string? Username { get; set; }
    public string? NewPassword { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public bool? UseProviderGuide { get; set; }
    public bool SupportsWebPlayback { get; set; }
    public int MaxConcurrentStreams { get; set; }
    public string DefaultChannelKind { get; set; } = "Tv";
    public string? CountryFilter { get; set; }
    public bool EnableHealthCheck { get; set; }
}

public class UpdateIptvPlaylistDto : CreateIptvPlaylistDto
{
    public bool IsActive { get; set; }
    public bool ClearPassword { get; set; }
}

public class TestIptvPlaylistConnectionDto : UpdateIptvPlaylistDto
{
    public Guid? PlaylistId { get; set; }
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

        group.MapGet("/playlists", GetAllPlaylistsAsync)
            .Produces<IEnumerable<IptvPlaylistVM>>(StatusCodes.Status200OK);
        group.MapPost("/playlists", AddPlaylistAsync)
            .Produces<IptvPlaylistVM>(StatusCodes.Status200OK);
        group.MapPost("/playlists/test-connection", TestPlaylistConnectionAsync)
            .WithName("TestIptvPlaylistConnection")
            .Produces<IptvConnectionTestVM>(StatusCodes.Status200OK);
        group.MapPut("/playlists/{id:guid}", UpdatePlaylistAsync)
            .Produces<IptvPlaylistVM>(StatusCodes.Status200OK);
        group.MapPost("/playlists/{id:guid}/refresh", RefreshPlaylistAsync);
        group.MapDelete("/playlists/{id:guid}", DeletePlaylistAsync);

        group.MapGet("/epg-sources", GetAllEpgSourcesAsync)
            .Produces<IEnumerable<IptvEpgSourceVM>>(StatusCodes.Status200OK);
        group.MapPost("/epg-sources", AddEpgSourceAsync)
            .Produces<IptvEpgSourceVM>(StatusCodes.Status200OK);
        group.MapPut("/epg-sources/{id:guid}", UpdateEpgSourceAsync)
            .Produces<IptvEpgSourceVM>(StatusCodes.Status200OK);
        group.MapPost("/epg-sources/{id:guid}/refresh", RefreshEpgSourceAsync);
        group.MapDelete("/epg-sources/{id:guid}", DeleteEpgSourceAsync);
        group.MapGet("/epg-diagnostics", GetEpgDiagnosticsAsync)
            .Produces<IptvEpgDiagnosticsVM>(StatusCodes.Status200OK);

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
        var playlist = await manager.AddPlaylistAsync(Settings(request, request.Name, defaultKind, isActive: true, clearPassword: false));
        return Results.Ok(playlist);
    }

    private static async Task<IResult> TestPlaylistConnectionAsync([FromBody] TestIptvPlaylistConnectionDto request, IIptvManager manager, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<IptvChannelKind>(request.DefaultChannelKind, ignoreCase: true, out var defaultKind))
        {
            return Results.BadRequest($"Unknown default channel kind: {request.DefaultChannelKind}");
        }
        var name = string.IsNullOrWhiteSpace(request.Name) ? "Connection test" : request.Name;
        var result = await manager.TestConnectionAsync(Settings(request, name, defaultKind, isActive: true, request.ClearPassword), request.PlaylistId, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdatePlaylistAsync(Guid id, [FromBody] UpdateIptvPlaylistDto request, IIptvManager manager)
    {
        if (!Enum.TryParse<IptvChannelKind>(request.DefaultChannelKind, ignoreCase: true, out var defaultKind))
        {
            return Results.BadRequest($"Unknown default channel kind: {request.DefaultChannelKind}");
        }
        var playlist = await manager.UpdatePlaylistAsync(id, Settings(request, request.Name, defaultKind, request.IsActive, request.ClearPassword));
        return Results.Ok(playlist);
    }

    private static IptvPlaylistSettingsDto Settings(CreateIptvPlaylistDto request, string name, IptvChannelKind defaultKind, bool isActive, bool clearPassword) => new()
    {
        Name = name,
        SourceType = request.SourceType,
        M3uUrl = request.M3uUrl,
        ServerUrl = request.ServerUrl,
        Username = request.Username,
        NewPassword = request.NewPassword,
        ClearPassword = clearPassword,
        UserAgent = request.UserAgent,
        Referer = request.Referer,
        UseProviderGuide = request.UseProviderGuide,
        SupportsWebPlayback = request.SupportsWebPlayback,
        MaxConcurrentStreams = request.MaxConcurrentStreams,
        IsActive = isActive,
        DefaultChannelKind = defaultKind,
        CountryFilter = request.CountryFilter,
        EnableHealthCheck = request.EnableHealthCheck
    };

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

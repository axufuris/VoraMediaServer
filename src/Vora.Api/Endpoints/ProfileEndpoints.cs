using Microsoft.AspNetCore.Mvc;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;

namespace Vora.Api.Endpoints;

public class CreateProfileDto
{
    public required string Name { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? Pin { get; set; }
    public bool HasAllLibraryAccess { get; set; } = true;
    public bool BlockUnratedContent { get; set; } = false;
    public List<Guid> AllowedLibraryIds { get; set; } = new();
    public List<string> AllowedMovieRatings { get; set; } = new();
    public List<string> AllowedTvRatings { get; set; } = new();
    public List<string> AllowedMusicRatings { get; set; } = new();
    public List<ProfileScheduleVM> AccessSchedules { get; set; } = new();
    public bool HasAllIptvAccess { get; set; } = true;
    public List<Guid> AllowedIptvPlaylistIds { get; set; } = new();
    public bool CanAddCustomPodcastFeeds { get; set; } = true;
}

public class UpdateProfileDto : CreateProfileDto
{
}

public class ValidatePinDto
{
    public required string Pin { get; set; }
}

public class UpdateClientSettingsDto
{
    public required string PlaybackPrefs { get; set; }
    public required string IptvPrefsJson { get; set; }
}

public class UpdateIptvPrefsDto
{
    public required string IptvPrefsJson { get; set; }
}

public class UpdateRadioPrefsDto
{
    public required string RadioPrefsJson { get; set; }
}

public class UpdateNavPrefsDto
{
    public required string NavPrefsJson { get; set; }
}

public class UpdateDiscoveryLayoutDto
{
    public required string DiscoveryLayoutJson { get; set; }
}

public class UpdateHomeLayoutDto
{
    public string LayoutJson { get; set; } = string.Empty;
}

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder routes)
    {
        MapManagedProfileEndpoints(routes);
        MapDevicePreferenceEndpoints(routes);
        return routes;
    }

    private static void MapManagedProfileEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").WithTags("Profiles").RequireAuthorization();

        group.MapPost("/{userId:guid}/profiles", CreateProfileAsync)
            .Produces(StatusCodes.Status201Created);

        group.MapPost("/profiles/{profileId:guid}/validate-pin", ValidatePinAsync)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/profiles/{profileId:guid}", UpdateProfileAsync);
        group.MapDelete("/profiles/{profileId:guid}", DeleteProfileAsync);
    }

    private static void MapDevicePreferenceEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users/profiles/{profileId:guid}/devices/{deviceId}")
            .WithTags("Profile Device Preferences")
            .RequireAuthorization();

        group.MapGet("/nav", GetNavPrefsAsync);
        group.MapPut("/nav", SaveNavPrefsAsync);

        group.MapGet("/iptv", GetIptvPrefsAsync);
        group.MapPut("/iptv", SaveIptvPrefsAsync);

        group.MapGet("/radio", GetRadioPrefsAsync);
        group.MapPut("/radio", SaveRadioPrefsAsync);

        group.MapGet("/playback", GetPlaybackPrefsAsync);

        group.MapGet("/discovery-layout", GetDiscoveryLayoutAsync);
        group.MapPut("/discovery-layout", SaveDiscoveryLayoutAsync);

        group.MapGet("/home-layout", GetHomeLayoutAsync);
        group.MapPut("/home-layout", SaveHomeLayoutAsync);

        group.MapPut("/settings", SaveClientSettingsAsync);
    }

    private static async Task<IResult> CreateProfileAsync(Guid userId, [FromBody] CreateProfileDto request, IUserManager manager)
    {
        var profileId = await manager.CreateManagedProfileAsync(
            userId,
            request.Name,
            request.ProfileImageUrl,
            request.Pin,
            request.AllowedMovieRatings,
            request.AllowedTvRatings,
            request.AllowedMusicRatings,
            request.HasAllLibraryAccess,
            request.BlockUnratedContent,
            request.AllowedLibraryIds,
            request.HasAllIptvAccess,
            request.AllowedIptvPlaylistIds,
            request.AccessSchedules,
            request.CanAddCustomPodcastFeeds);

        return Results.Created($"/api/users/{userId}", new { ProfileId = profileId });
    }

    private static async Task<IResult> ValidatePinAsync(Guid profileId, [FromBody] ValidatePinDto request, IUserManager manager)
    {
        var isValid = await manager.ValidateProfilePinAsync(profileId, request.Pin);
        return isValid ? Results.Ok(new { Success = true }) : Results.Unauthorized();
    }

    private static async Task<IResult> UpdateProfileAsync(Guid profileId, [FromBody] UpdateProfileDto request, IUserManager manager)
    {
        await manager.UpdateManagedProfileAsync(
            profileId,
            request.Name,
            request.ProfileImageUrl,
            request.Pin,
            request.AllowedMovieRatings,
            request.AllowedTvRatings,
            request.AllowedMusicRatings,
            request.HasAllLibraryAccess,
            request.BlockUnratedContent,
            request.AllowedLibraryIds,
            request.HasAllIptvAccess,
            request.AllowedIptvPlaylistIds,
            request.AccessSchedules,
            request.CanAddCustomPodcastFeeds);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteProfileAsync(Guid profileId, IUserManager manager)
    {
        await manager.DeleteManagedProfileAsync(profileId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetNavPrefsAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var json = await manager.GetProfileDeviceNavPrefsAsync(profileId, deviceId);
        return Results.Ok(new { NavPrefsJson = json });
    }

    private static async Task<IResult> SaveNavPrefsAsync(Guid profileId, string deviceId, [FromBody] UpdateNavPrefsDto request, IUserManager manager)
    {
        await manager.SaveProfileDeviceNavPrefsAsync(profileId, deviceId, request.NavPrefsJson);
        return Results.NoContent();
    }

    private static async Task<IResult> GetIptvPrefsAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var json = await manager.GetProfileDeviceIptvPrefsAsync(profileId, deviceId);
        return Results.Ok(new { IptvPrefsJson = json });
    }

    private static async Task<IResult> SaveIptvPrefsAsync(Guid profileId, string deviceId, [FromBody] UpdateIptvPrefsDto request, IUserManager manager)
    {
        var existingPlayback = await manager.GetProfileDevicePlaybackPrefsAsync(profileId, deviceId) ?? string.Empty;
        await manager.SaveProfileDeviceSettingsAsync(profileId, deviceId, existingPlayback, request.IptvPrefsJson);
        return Results.NoContent();
    }

    private static async Task<IResult> GetRadioPrefsAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var json = await manager.GetProfileDeviceRadioPrefsAsync(profileId, deviceId);
        return Results.Ok(new { RadioPrefsJson = json });
    }

    private static async Task<IResult> SaveRadioPrefsAsync(Guid profileId, string deviceId, [FromBody] UpdateRadioPrefsDto request, IUserManager manager)
    {
        await manager.SaveProfileDeviceRadioPrefsAsync(profileId, deviceId, request.RadioPrefsJson);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPlaybackPrefsAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var prefs = await manager.GetProfileDevicePlaybackPrefsAsync(profileId, deviceId);
        return Results.Ok(new { PlaybackPrefs = prefs });
    }

    private static async Task<IResult> GetDiscoveryLayoutAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var json = await manager.GetProfileDeviceDiscoveryLayoutAsync(profileId, deviceId);
        return Results.Ok(new { DiscoveryLayoutJson = json });
    }

    private static async Task<IResult> SaveDiscoveryLayoutAsync(Guid profileId, string deviceId, [FromBody] UpdateDiscoveryLayoutDto request, IUserManager manager)
    {
        await manager.SaveProfileDeviceDiscoveryLayoutAsync(profileId, deviceId, request.DiscoveryLayoutJson);
        return Results.NoContent();
    }

    private static async Task<IResult> GetHomeLayoutAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var layout = await manager.GetProfileDeviceHomeLayoutAsync(profileId, deviceId);
        return Results.Ok(new { HomeLayoutJson = layout });
    }

    private static async Task<IResult> SaveHomeLayoutAsync(Guid profileId, string deviceId, [FromBody] UpdateHomeLayoutDto request, IUserManager manager)
    {
        await manager.SaveProfileDeviceHomeLayoutAsync(profileId, deviceId, request.LayoutJson);
        return Results.NoContent();
    }

    private static async Task<IResult> SaveClientSettingsAsync(Guid profileId, string deviceId, [FromBody] UpdateClientSettingsDto request, IUserManager manager)
    {
        await manager.SaveProfileDeviceSettingsAsync(profileId, deviceId, request.PlaybackPrefs, request.IptvPrefsJson);
        return Results.NoContent();
    }
}

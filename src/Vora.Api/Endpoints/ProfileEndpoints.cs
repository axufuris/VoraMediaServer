using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;

namespace Vora.Api.Endpoints;

public class ShowtimesLocationDto
{
    public string? Location { get; set; }
}

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
    public string? ShowtimesLocation { get; set; }
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

public class IptvPrefsResponse
{
    public string? IptvPrefsJson { get; set; }
}

public class UpdateRadioPrefsDto
{
    public required string RadioPrefsJson { get; set; }
}

public class RadioPrefsResponse
{
    public string? RadioPrefsJson { get; set; }
}

public class NavPrefsResponse
{
    public string? NavPrefsJson { get; set; }
}

public class UpdateNavPrefsDto
{
    public required string NavPrefsJson { get; set; }
}

public class UpdateDiscoveryLayoutDto
{
    public required string DiscoveryLayoutJson { get; set; }
}

public class DiscoveryLayoutResponse
{
    public string? DiscoveryLayoutJson { get; set; }
}

public class UpdateHomeLayoutDto
{
    public string LayoutJson { get; set; } = string.Empty;
}

public class HomeLayoutResponse
{
    public string? HomeLayoutJson { get; set; }
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
        var group = routes.MapGroup("/api/users").WithTags("Profiles").RequireAuthorization()
            .AddEndpointFilter<AccountOwnershipFilter>();

        group.MapPost("/{userId:guid}/profiles", CreateProfileAsync)
            .WithName("CreateProfile")
            .Produces(StatusCodes.Status201Created);

        group.MapPost("/profiles/{profileId:guid}/validate-pin", ValidatePinAsync)
            .WithName("ValidateProfilePin")
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/profiles/{profileId:guid}", UpdateProfileAsync)
            .WithName("UpdateProfile")
            .Produces(StatusCodes.Status204NoContent);
        group.MapDelete("/profiles/{profileId:guid}", DeleteProfileAsync)
            .WithName("DeleteProfile")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/profiles/me/showtimes-location", GetMyShowtimesLocationAsync)
            .WithName("GetMyShowtimesLocation")
            .Produces<ShowtimesLocationDto>(StatusCodes.Status200OK);
        group.MapPut("/profiles/me/showtimes-location", SaveMyShowtimesLocationAsync)
            .WithName("SaveMyShowtimesLocation")
            .Produces<ShowtimesLocationDto>(StatusCodes.Status200OK);

        group.MapPut("/profiles/{profileId:guid}/showtimes-location", AdminSetShowtimesLocationAsync)
            .RequireAuthorization("AdminOnly")
            .Produces<ShowtimesLocationDto>(StatusCodes.Status200OK);

        group.MapGet("/profiles/me/playback-preferences", GetMyPlaybackPreferencesAsync)
            .WithName("GetMyPlaybackPreferences")
            .Produces<PlaybackPreferencesVM>(StatusCodes.Status200OK);
        group.MapPut("/profiles/me/playback-preferences", SaveMyPlaybackPreferencesAsync)
            .WithName("SaveMyPlaybackPreferences")
            .Produces<PlaybackPreferencesVM>(StatusCodes.Status200OK);

        group.MapGet("/profiles/{profileId:guid}/radio-prefs", GetProfileRadioPrefsAsync)
            .WithName("GetProfileRadioPrefs")
            .Produces<RadioPrefsResponse>(StatusCodes.Status200OK);
        group.MapPut("/profiles/{profileId:guid}/radio-prefs", SaveProfileRadioPrefsAsync)
            .WithName("SaveProfileRadioPrefs")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapDevicePreferenceEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users/profiles/{profileId:guid}/devices/{deviceId}")
            .WithTags("Profile Device Preferences")
            .RequireAuthorization()
            .AddEndpointFilter<AccountOwnershipFilter>();

        group.MapGet("/nav", GetNavPrefsAsync)
            .WithName("GetNavPrefs")
            .Produces<NavPrefsResponse>(StatusCodes.Status200OK);
        group.MapPut("/nav", SaveNavPrefsAsync)
            .WithName("SaveNavPrefs")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/iptv", GetIptvPrefsAsync)
            .WithName("GetIptvPrefs")
            .Produces<IptvPrefsResponse>(StatusCodes.Status200OK);
        group.MapPut("/iptv", SaveIptvPrefsAsync)
            .WithName("SaveIptvPrefs")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/radio", GetRadioPrefsAsync)
            .WithName("GetRadioPrefs")
            .Produces<RadioPrefsResponse>(StatusCodes.Status200OK);
        group.MapPut("/radio", SaveRadioPrefsAsync)
            .WithName("SaveRadioPrefs")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/playback", GetPlaybackPrefsAsync);

        group.MapGet("/discovery-layout", GetDiscoveryLayoutAsync)
            .WithName("GetDiscoveryLayout")
            .Produces<DiscoveryLayoutResponse>(StatusCodes.Status200OK);
        group.MapPut("/discovery-layout", SaveDiscoveryLayoutAsync)
            .WithName("SaveDiscoveryLayout")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/home-layout", GetHomeLayoutAsync)
            .WithName("GetHomeLayout")
            .Produces<HomeLayoutResponse>(StatusCodes.Status200OK);
        group.MapPut("/home-layout", SaveHomeLayoutAsync)
            .WithName("SaveHomeLayout")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPut("/settings", SaveClientSettingsAsync)
            .WithName("SaveClientSettings");
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
            request.CanAddCustomPodcastFeeds,
            request.ShowtimesLocation);

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
            request.CanAddCustomPodcastFeeds,
            request.ShowtimesLocation);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteProfileAsync(Guid profileId, IUserManager manager)
    {
        await manager.DeleteManagedProfileAsync(profileId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetMyShowtimesLocationAsync(HttpContext ctx, IUserManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();
        var location = await manager.GetShowtimesLocationAsync(profileId.Value);
        return Results.Ok(new ShowtimesLocationDto { Location = location });
    }

    private static async Task<IResult> SaveMyShowtimesLocationAsync(HttpContext ctx, [FromBody] ShowtimesLocationDto body, IUserManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();
        await manager.SaveShowtimesLocationAsync(profileId.Value, body.Location);
        return Results.Ok(new ShowtimesLocationDto { Location = string.IsNullOrWhiteSpace(body.Location) ? null : body.Location.Trim() });
    }

    private static async Task<IResult> AdminSetShowtimesLocationAsync(Guid profileId, [FromBody] ShowtimesLocationDto body, IUserManager manager)
    {
        try
        {
            await manager.SaveShowtimesLocationAsync(profileId, body.Location);
            return Results.Ok(new ShowtimesLocationDto { Location = string.IsNullOrWhiteSpace(body.Location) ? null : body.Location.Trim() });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { Message = ex.Message });
        }
    }

    private static async Task<IResult> GetMyPlaybackPreferencesAsync(HttpContext ctx, IUserManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();
        var prefs = await manager.GetPlaybackPreferencesAsync(profileId.Value);
        return Results.Ok(prefs);
    }

    private static async Task<IResult> SaveMyPlaybackPreferencesAsync(HttpContext ctx, [FromBody] PlaybackPreferencesVM body, IUserManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();
        var saved = await manager.SavePlaybackPreferencesAsync(profileId.Value, body);
        return Results.Ok(saved);
    }

    private static async Task<IResult> GetNavPrefsAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var json = await manager.GetProfileDeviceNavPrefsAsync(profileId, deviceId);
        return Results.Ok(new NavPrefsResponse { NavPrefsJson = json });
    }

    private static async Task<IResult> SaveNavPrefsAsync(Guid profileId, string deviceId, [FromBody] UpdateNavPrefsDto request, IUserManager manager)
    {
        await manager.SaveProfileDeviceNavPrefsAsync(profileId, deviceId, request.NavPrefsJson);
        return Results.NoContent();
    }

    private static async Task<IResult> GetIptvPrefsAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var json = await manager.GetProfileDeviceIptvPrefsAsync(profileId, deviceId);
        return Results.Ok(new IptvPrefsResponse { IptvPrefsJson = json });
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
        return Results.Ok(new RadioPrefsResponse { RadioPrefsJson = json });
    }

    private static async Task<IResult> GetProfileRadioPrefsAsync(Guid profileId, IUserManager manager)
    {
        var json = await manager.GetProfileRadioPrefsAsync(profileId);
        return Results.Ok(new RadioPrefsResponse { RadioPrefsJson = json });
    }

    private static async Task<IResult> SaveProfileRadioPrefsAsync(Guid profileId, [FromBody] UpdateRadioPrefsDto request, IUserManager manager)
    {
        try
        {
            await manager.SaveProfileRadioPrefsAsync(profileId, request.RadioPrefsJson);
            return Results.NoContent();
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
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
        return Results.Ok(new DiscoveryLayoutResponse { DiscoveryLayoutJson = json });
    }

    private static async Task<IResult> SaveDiscoveryLayoutAsync(Guid profileId, string deviceId, [FromBody] UpdateDiscoveryLayoutDto request, IUserManager manager)
    {
        await manager.SaveProfileDeviceDiscoveryLayoutAsync(profileId, deviceId, request.DiscoveryLayoutJson);
        return Results.NoContent();
    }

    private static async Task<IResult> GetHomeLayoutAsync(Guid profileId, string deviceId, IUserManager manager)
    {
        var layout = await manager.GetProfileDeviceHomeLayoutAsync(profileId, deviceId);
        return Results.Ok(new HomeLayoutResponse { HomeLayoutJson = layout });
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

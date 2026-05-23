using Vora.Application.YouTube.Dtos;
using Vora.Domain.Entities.YouTube;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.YouTube;

namespace Vora.Application.YouTube;

public interface IYouTubeAccessResolver
{
    Task<YouTubeAccessResolution> ResolveAsync(Guid profileId);
}

public sealed class YouTubeAccessResolution
{
    public bool IsAvailable { get; init; }
    public string? DeniedReason { get; init; }
    public YouTubeSafeSearchLevel SafeSearch { get; init; } = YouTubeSafeSearchLevel.Moderate;
    public bool FilterAgeRestricted { get; init; }
    public bool BlockUnratedContent { get; init; }
    public List<string> AllowedMovieRatings { get; init; } = new();
    public List<string> AllowedTvRatings { get; init; } = new();
    public bool HasAllRatings { get; init; } = true;

    public static YouTubeAccessResolution Denied(string reason) =>
        new() { IsAvailable = false, DeniedReason = reason };
}

public sealed class YouTubeAccessResolver(
    IYouTubeAccessRepository repository,
    IYouTubeDataApiClient apiClient,
    IPluginSettingsProvider settings,
    IEnumerable<IVoraPlugin> plugins) : IYouTubeAccessResolver
{
    public async Task<YouTubeAccessResolution> ResolveAsync(Guid profileId)
    {
        var pluginInstalled = plugins.Any(p => string.Equals(p.Id, YouTubePlugin.PluginId, StringComparison.OrdinalIgnoreCase));
        if (!pluginInstalled)
        {
            return YouTubeAccessResolution.Denied("YouTube plugin is not installed.");
        }

        if (!await apiClient.IsConfiguredAsync())
        {
            return YouTubeAccessResolution.Denied("YouTube Data API key is not configured.");
        }

        var serverEnabledRaw = await settings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.IsEnabledSettingKey);
        var serverEnabled = !string.Equals(serverEnabledRaw, "false", StringComparison.OrdinalIgnoreCase);
        if (!serverEnabled)
        {
            return YouTubeAccessResolution.Denied("YouTube is disabled server-wide.");
        }

        var profile = await repository.GetProfileWithUserAsync(profileId);
        if (profile is null)
        {
            return YouTubeAccessResolution.Denied("Profile not found.");
        }

        var accountSettings = await repository.GetAccountSettingsAsync(profile.UserId);
        if (accountSettings is not null && accountSettings.YouTubeAccess == YouTubeAccessSetting.Disabled)
        {
            return YouTubeAccessResolution.Denied("YouTube is disabled for this account.");
        }

        var profileSettings = await repository.GetProfileSettingsAsync(profile.Id);
        if (profileSettings is not null && !profileSettings.IsEnabled)
        {
            return YouTubeAccessResolution.Denied("YouTube is disabled for this profile.");
        }

        var hasAllRatings = profile.AllowedMovieRatings.Count == 0
            && profile.AllowedTvRatings.Count == 0
            && profile.AllowedMusicRatings.Count == 0;

        var parentalControlsActive = !hasAllRatings || profile.BlockUnratedContent;

        return new YouTubeAccessResolution
        {
            IsAvailable = true,
            SafeSearch = parentalControlsActive ? YouTubeSafeSearchLevel.Strict : YouTubeSafeSearchLevel.Moderate,
            FilterAgeRestricted = parentalControlsActive,
            BlockUnratedContent = profile.BlockUnratedContent,
            AllowedMovieRatings = profile.AllowedMovieRatings.ToList(),
            AllowedTvRatings = profile.AllowedTvRatings.ToList(),
            HasAllRatings = hasAllRatings
        };
    }
}

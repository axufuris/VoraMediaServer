using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.YouTube;

public class YouTubePlugin : IVoraPlugin
{
    public const string PluginId = "youtube";
    public const string ApiKeySettingKey = "api_key";
    public const string IsEnabledSettingKey = "is_enabled";
    public const string TrendingRegionSettingKey = "trending_region";

    public string Id => PluginId;
    public string Name => "YouTube";
    public string ProviderName => "YouTube";
    public string Version => "1.0.0";
    public string Description => "Browse, search, and watch YouTube content from inside Vora using the official YouTube Data API and iframe player. Personalisation (subscriptions, watch history) stays inside Vora and never touches a Google account.";
    public bool IsSystemPlugin => true;
    public string Type => "YouTube";
    public string? DeveloperName => "Vora";
    public string? DocumentationUrl => "https://developers.google.com/youtube/v3/getting-started";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = ApiKeySettingKey,
                Label = "YouTube Data API Key",
                Type = "password",
                Description = "YouTube Data API v3 key (free). Three-step setup: (1) Create a Google Cloud project at https://console.cloud.google.com/projectcreate. (2) Enable the YouTube Data API v3 at https://console.cloud.google.com/apis/library/youtube.googleapis.com. (3) Open https://console.cloud.google.com/apis/credentials, click 'Create Credentials → API key', and paste the result here. The free quota is 10,000 units/day, more than enough for a personal or small-family server."
            },
            new PluginSettingDefinitionDto
            {
                Key = TrendingRegionSettingKey,
                Label = "Trending Region",
                Type = "text",
                DefaultValue = "US",
                Description = "ISO 3166-1 country code used for the Trending rail (e.g. US, GB, CA). Defaults to US if blank."
            }
        };
    }
}

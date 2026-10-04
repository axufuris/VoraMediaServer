using System.Text.Json;
using System.Text.Json.Nodes;
using Vora.Application.Analysis;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public interface IChannelFavoritesManager
{
    Task<string?> MergeTvFavoritesAsync(Guid profileId, string? iptvPrefsJson);
    Task<string> SyncTvFavoritesAsync(Guid profileId, string iptvPrefsJson);
    Task<string?> MergeRadioFavoritesAsync(Guid profileId, string? radioPrefsJson);
    Task<string> SyncRadioFavoritesAsync(Guid profileId, string radioPrefsJson);
}

public class ChannelFavoritesManager(IChannelFavoriteRepository repository, IClientNotifier notifier) : IChannelFavoritesManager
{
    public const string TvFavoritesProperty = "favoriteChannels";
    public const string RadioFavoritesProperty = "favoriteIds";
    private const string EnabledProvidersProperty = "enabledProviders";

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string?> MergeTvFavoritesAsync(Guid profileId, string? iptvPrefsJson)
    {
        var favorites = await repository.GetFavoriteChannelsAsync(profileId, IptvChannelKind.Tv);
        var externalIds = favorites
            .OrderBy(f => f.AddedAt)
            .Select(f => f.Channel.ExternalChannelId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return WriteFavorites(iptvPrefsJson, TvFavoritesProperty, externalIds);
    }

    public async Task<string> SyncTvFavoritesAsync(Guid profileId, string iptvPrefsJson)
    {
        if (!TryTakeFavorites(iptvPrefsJson, TvFavoritesProperty, out var prefs, out var values))
        {
            return iptvPrefsJson;
        }

        var externalIds = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var channels = externalIds.Count == 0
            ? []
            : await repository.FindChannelsByExternalIdsAsync(externalIds, IptvChannelKind.Tv);

        await ReplaceAsync(profileId, IptvChannelKind.Tv, channels.Select(c => new ChannelFavoriteKey(c.PlaylistId, c.ExternalChannelId)));
        return prefs.ToJsonString();
    }

    public async Task<string?> MergeRadioFavoritesAsync(Guid profileId, string? radioPrefsJson)
    {
        var favorites = await repository.GetFavoriteChannelsAsync(profileId, IptvChannelKind.Radio);
        var channelIds = favorites
            .OrderBy(f => f.AddedAt)
            .Select(f => f.Channel.Id.ToString())
            .Distinct()
            .ToList();

        return WriteFavorites(radioPrefsJson, RadioFavoritesProperty, channelIds);
    }

    public async Task<string> SyncRadioFavoritesAsync(Guid profileId, string radioPrefsJson)
    {
        if (!TryTakeFavorites(radioPrefsJson, RadioFavoritesProperty, out var prefs, out var values))
        {
            return radioPrefsJson;
        }

        var channelIds = values
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var channels = channelIds.Count == 0
            ? []
            : await repository.FindChannelsByIdsAsync(channelIds, IptvChannelKind.Radio);

        await ReplaceAsync(profileId, IptvChannelKind.Radio, channels.Select(c => new ChannelFavoriteKey(c.PlaylistId, c.ExternalChannelId)));
        return prefs.ToJsonString();
    }

    private async Task ReplaceAsync(Guid profileId, IptvChannelKind kind, IEnumerable<ChannelFavoriteKey> keys)
    {
        var distinct = keys.DistinctBy(k => (k.PlaylistId, k.ExternalChannelId)).ToList();
        if (await repository.ReplaceFavoritesAsync(profileId, kind, distinct))
        {
            await notifier.NotifyChannelFavoritesUpdatedAsync(profileId);
        }
    }

    private static string? WriteFavorites(string? prefsJson, string property, List<string> favorites)
    {
        var node = ParseOrNull(prefsJson);

        if (node is JsonArray providers)
        {
            if (favorites.Count == 0) return prefsJson;
            node = new JsonObject(NodeOptions) { [EnabledProvidersProperty] = providers.DeepClone() };
        }

        if (node is not JsonObject prefs)
        {
            if (favorites.Count == 0) return prefsJson;
            prefs = new JsonObject(NodeOptions);
        }

        prefs[property] = new JsonArray(favorites.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray());
        return prefs.ToJsonString();
    }

    private static bool TryTakeFavorites(string prefsJson, string property, out JsonObject prefs, out List<string> values)
    {
        values = [];
        prefs = new JsonObject(NodeOptions);

        if (ParseOrNull(prefsJson) is not JsonObject parsed || !parsed.TryGetPropertyValue(property, out var raw) || raw is not JsonArray array)
        {
            return false;
        }

        values = array
            .OfType<JsonValue>()
            .Select(v => v.TryGetValue<string>(out var text) ? text.Trim() : string.Empty)
            .Where(v => v.Length > 0)
            .ToList();

        parsed.Remove(property);
        prefs = parsed;
        return true;
    }

    private static JsonNode? ParseOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonNode.Parse(json, NodeOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

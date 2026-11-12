using Vora.Application.Iptv.ViewModels;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public static class IptvConnectionLimit
{
    public static int Of(int? maxConcurrentStreams, int? accountMaxConnections) =>
        maxConcurrentStreams is > 0 ? maxConcurrentStreams.Value : Math.Max(accountMaxConnections ?? 0, 0);

    public static int Of(IptvPlaylist playlist) =>
        Of(playlist.TunerProfile?.MaxConcurrentStreams, playlist.AccountMaxConnections);
}

public static class IptvPlaylistView
{
    public const string ChannelReferenceScheme = "vora-channel:";
    public const string PlaylistReferenceScheme = "vora-playlist:";

    public static string ClientM3uUrl(IptvPlaylist playlist)
    {
        var address = playlist.SourceType == IptvSourceType.Xtream ? playlist.ServerUrl : playlist.M3uUrl;
        return IptvUrlRedactor.SchemeAndHost(address) ?? PlaylistReferenceScheme + playlist.Id;
    }

    public static string ClientStreamUrl(IptvChannel channel) => ChannelReferenceScheme + channel.Id;

    public static IptvChannelVM ClientChannel(IptvChannel channel, IptvPlaylist? playlist)
    {
        var view = IptvChannelVM.FromEntity(channel, playlist?.Name ?? string.Empty);
        view.StreamUrl = ClientStreamUrl(channel);
        return view;
    }

    public static IptvPlaylistVM ForAdmin(IptvPlaylist entity) =>
        Build(entity, entity.Channels?.AsEnumerable() ?? Enumerable.Empty<IptvChannel>(), c => IptvChannelVM.FromEntity(c, entity.Name));

    public static IptvPlaylistVM ForClient(IptvPlaylist entity)
    {
        var channels = entity.Channels?.AsEnumerable() ?? Enumerable.Empty<IptvChannel>();
        if (!string.IsNullOrWhiteSpace(entity.CountryFilter))
        {
            channels = channels.Where(c => string.Equals(c.CountryCode, entity.CountryFilter, StringComparison.OrdinalIgnoreCase));
        }
        channels = channels.Where(c => c.IsHealthy != false);

        var view = Build(entity, channels, c => ClientChannel(c, entity));
        view.M3uUrl = ClientM3uUrl(entity);
        view.ServerUrl = null;
        view.Username = null;
        view.PasswordIsSet = false;
        view.UserAgent = null;
        view.Referer = null;
        view.AccountStatus = null;
        view.AccountExpiresAt = null;
        view.AccountMaxConnections = null;
        view.AccountCheckedAt = null;
        view.LastError = null;
        return view;
    }

    private static IptvPlaylistVM Build(IptvPlaylist entity, IEnumerable<IptvChannel> channels, Func<IptvChannel, IptvChannelVM> channelView) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        SourceType = entity.SourceType.ToString(),
        M3uUrl = entity.M3uUrl,
        ServerUrl = entity.ServerUrl,
        Username = entity.Username,
        PasswordIsSet = !string.IsNullOrEmpty(entity.PasswordCiphertext),
        UserAgent = entity.UserAgent,
        Referer = entity.Referer,
        UseProviderGuide = entity.UseProviderGuide,
        AccountStatus = entity.AccountStatus,
        AccountExpiresAt = entity.AccountExpiresAt,
        AccountMaxConnections = entity.AccountMaxConnections,
        AccountCheckedAt = entity.AccountCheckedAt,
        IsActive = entity.IsActive,
        SupportsWebPlayback = entity.SupportsWebPlayback,
        MaxConcurrentStreams = entity.TunerProfile?.MaxConcurrentStreams ?? 0,
        ConnectionLimit = IptvConnectionLimit.Of(entity),
        LastError = entity.LastError,
        LastSyncedAt = entity.LastSyncedAt,
        DefaultChannelKind = entity.DefaultChannelKind.ToString(),
        CountryFilter = entity.CountryFilter,
        EnableHealthCheck = entity.EnableHealthCheck,
        Channels = channels.Select(channelView).ToList()
    };
}

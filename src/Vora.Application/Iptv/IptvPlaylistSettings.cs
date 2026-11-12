using Vora.Application.Iptv.Dtos;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public sealed record IptvPlaylistChange(bool SourceChanged, bool GuideChanged);

public static class IptvPlaylistSettings
{
    private const int MaxNameLength = 128;
    private const int MaxUrlLength = 1024;
    private const int MaxUsernameLength = 256;

    public static IptvPlaylistChange Apply(IptvPlaylist playlist, IptvPlaylistSettingsDto settings, IIptvSecretProtector protector)
    {
        var before = Snapshot(playlist);

        var name = settings.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("The playlist needs a name.");
        playlist.Name = Limit(name, MaxNameLength, "name");

        playlist.SourceType = settings.SourceType ?? playlist.SourceType;

        if (settings.M3uUrl is not null) playlist.M3uUrl = Blank(settings.M3uUrl) is { } url ? Limit(url, MaxUrlLength, "M3U address") : null;
        if (settings.ServerUrl is not null)
        {
            if (Blank(settings.ServerUrl) is { } server)
            {
                playlist.ServerUrl = Limit(XtreamAccount.NormalizeServerUrl(server) ?? throw new ArgumentException("The server address isn't a valid web address."), MaxUrlLength, "server address");
            }
            else
            {
                playlist.ServerUrl = null;
            }
        }

        if (settings.Username is not null) playlist.Username = Blank(settings.Username) is { } username ? Limit(username, MaxUsernameLength, "username") : null;

        if (settings.ClearPassword)
        {
            playlist.PasswordCiphertext = null;
        }
        else if (!string.IsNullOrEmpty(settings.NewPassword))
        {
            playlist.PasswordCiphertext = protector.Protect(settings.NewPassword);
        }

        if (settings.UserAgent is not null) playlist.UserAgent = IptvRequestPolicy.CleanHeaderValue(settings.UserAgent, IptvRequestPolicy.MaxUserAgentLength);
        if (settings.Referer is not null) playlist.Referer = IptvRequestPolicy.CleanHeaderValue(settings.Referer, IptvRequestPolicy.MaxRefererLength);
        if (settings.UseProviderGuide is { } useGuide) playlist.UseProviderGuide = useGuide;

        playlist.SupportsWebPlayback = settings.SupportsWebPlayback;
        playlist.IsActive = settings.IsActive;
        playlist.DefaultChannelKind = settings.DefaultChannelKind;
        playlist.CountryFilter = NormalizeCountryFilter(settings.CountryFilter);
        playlist.EnableHealthCheck = settings.EnableHealthCheck;

        if (playlist.TunerProfile == null)
        {
            playlist.TunerProfile = new IptvTunerProfile { PlaylistId = playlist.Id, MaxConcurrentStreams = Math.Max(settings.MaxConcurrentStreams, 0) };
        }
        else
        {
            playlist.TunerProfile.MaxConcurrentStreams = Math.Max(settings.MaxConcurrentStreams, 0);
        }

        Validate(playlist);

        if (playlist.SourceType != IptvSourceType.Xtream)
        {
            ForgetAccount(playlist);
        }

        var after = Snapshot(playlist);
        return new IptvPlaylistChange(
            !before.SameSourceAs(after),
            before.UseProviderGuide != after.UseProviderGuide || before.IsActive != after.IsActive);
    }

    public static void ForgetAccount(IptvPlaylist playlist)
    {
        playlist.AccountStatus = null;
        playlist.AccountExpiresAt = null;
        playlist.AccountMaxConnections = null;
        playlist.AccountCheckedAt = null;
    }

    public static void RecordAccount(IptvPlaylist playlist, XtreamAccountInfo info, DateTime checkedAt)
    {
        playlist.AccountStatus = info.Status.Length <= 32 ? info.Status : info.Status[..32];
        playlist.AccountExpiresAt = info.ExpiresAt;
        playlist.AccountMaxConnections = info.MaxConnections is > 0 ? info.MaxConnections : null;
        playlist.AccountCheckedAt = checkedAt;
    }

    public static string? NormalizeCountryFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return string.Equals(trimmed, "All", StringComparison.OrdinalIgnoreCase) ? null : trimmed.ToUpperInvariant();
    }

    private static void Validate(IptvPlaylist playlist)
    {
        if (playlist.SourceType == IptvSourceType.Xtream)
        {
            if (string.IsNullOrEmpty(playlist.ServerUrl)) throw new ArgumentException("An Xtream Codes account needs the server address.");
            if (string.IsNullOrEmpty(playlist.Username)) throw new ArgumentException("An Xtream Codes account needs the username.");
            if (string.IsNullOrEmpty(playlist.PasswordCiphertext)) throw new ArgumentException("An Xtream Codes account needs the password.");
            return;
        }

        if (string.IsNullOrEmpty(playlist.M3uUrl)) throw new ArgumentException("An M3U playlist needs its address.");
        if (!string.IsNullOrEmpty(playlist.PasswordCiphertext) && string.IsNullOrEmpty(playlist.Username))
        {
            throw new ArgumentException("A password needs a username to go with it.");
        }
    }

    private sealed record SourceSnapshot(
        IptvSourceType SourceType,
        string? M3uUrl,
        string? ServerUrl,
        string? Username,
        string? PasswordCiphertext,
        string? UserAgent,
        string? Referer,
        IptvChannelKind DefaultChannelKind,
        bool UseProviderGuide,
        bool IsActive)
    {
        public bool SameSourceAs(SourceSnapshot other) =>
            SourceType == other.SourceType
            && string.Equals(M3uUrl, other.M3uUrl, StringComparison.Ordinal)
            && string.Equals(ServerUrl, other.ServerUrl, StringComparison.Ordinal)
            && string.Equals(Username, other.Username, StringComparison.Ordinal)
            && string.Equals(PasswordCiphertext, other.PasswordCiphertext, StringComparison.Ordinal)
            && string.Equals(UserAgent, other.UserAgent, StringComparison.Ordinal)
            && string.Equals(Referer, other.Referer, StringComparison.Ordinal)
            && DefaultChannelKind == other.DefaultChannelKind;
    }

    private static SourceSnapshot Snapshot(IptvPlaylist p) =>
        new(p.SourceType, p.M3uUrl, p.ServerUrl, p.Username, p.PasswordCiphertext, p.UserAgent, p.Referer, p.DefaultChannelKind, p.UseProviderGuide, p.IsActive);

    private static string? Blank(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string Limit(string value, int max, string what) =>
        value.Length <= max ? value : throw new ArgumentException($"The {what} is too long (at most {max} characters).");
}

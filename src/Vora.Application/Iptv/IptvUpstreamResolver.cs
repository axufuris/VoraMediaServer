using System.Security.Cryptography;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public sealed record IptvUpstream(string Url, IptvRequestPolicy Policy);

public interface IIptvUpstreamResolver
{
    IptvRequestPolicy PolicyFor(IptvPlaylist? playlist, IptvChannel? channel = null);
    IptvUpstream Resolve(IptvPlaylist? playlist, IptvChannel channel);
    XtreamAccount XtreamAccountFor(IptvPlaylist playlist);
    string? PasswordFor(IptvPlaylist playlist);
}

public class IptvUpstreamResolver : IIptvUpstreamResolver
{
    public const string UnreadablePasswordMessage = "The saved password can't be read on this server (its encryption keys changed). Enter the password again.";

    private readonly IIptvSecretProtector _protector;

    public IptvUpstreamResolver(IIptvSecretProtector protector)
    {
        _protector = protector;
    }

    public IptvRequestPolicy PolicyFor(IptvPlaylist? playlist, IptvChannel? channel = null)
    {
        var userAgent = channel?.UserAgent ?? playlist?.UserAgent;
        var referer = channel?.Referer ?? playlist?.Referer;
        var basic = playlist is { SourceType: IptvSourceType.M3u } && !string.IsNullOrEmpty(playlist.Username)
            ? IptvBasicCredentials.ForUrl(playlist.M3uUrl, playlist.Username, PasswordFor(playlist))
            : null;

        return userAgent is null && referer is null && basic is null
            ? IptvRequestPolicy.None
            : new IptvRequestPolicy(userAgent, referer, basic);
    }

    public IptvUpstream Resolve(IptvPlaylist? playlist, IptvChannel channel)
    {
        var policy = PolicyFor(playlist, channel);
        if (!XtreamStreamReference.TryParse(channel.StreamUrl, out var streamId, out var extension))
        {
            return new IptvUpstream(channel.StreamUrl, policy);
        }

        if (playlist is not { SourceType: IptvSourceType.Xtream })
        {
            throw new IptvSourceException("This channel belongs to an Xtream Codes account that is no longer set up.");
        }

        return new IptvUpstream(XtreamAccountFor(playlist).LiveStreamUrl(streamId, extension), policy);
    }

    public XtreamAccount XtreamAccountFor(IptvPlaylist playlist)
    {
        var server = XtreamAccount.NormalizeServerUrl(playlist.ServerUrl);
        if (server is null || string.IsNullOrWhiteSpace(playlist.Username))
        {
            throw new IptvSourceException("The Xtream Codes account needs a server address and a username.");
        }

        var password = PasswordFor(playlist) ?? throw new IptvSourceException("The Xtream Codes account has no password saved.");
        return new XtreamAccount(server, playlist.Username, password);
    }

    public string? PasswordFor(IptvPlaylist playlist)
    {
        if (string.IsNullOrEmpty(playlist.PasswordCiphertext)) return null;

        try
        {
            return _protector.Unprotect(playlist.PasswordCiphertext);
        }
        catch (CryptographicException)
        {
            throw new IptvSourceException(UnreadablePasswordMessage);
        }
    }
}

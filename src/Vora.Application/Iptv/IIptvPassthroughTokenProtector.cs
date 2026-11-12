namespace Vora.Application.Iptv;

public interface IIptvPassthroughTokenProtector
{
    string Protect(string payload, DateTimeOffset expiresAt);
    bool TryUnprotect(string token, out string payload);
}

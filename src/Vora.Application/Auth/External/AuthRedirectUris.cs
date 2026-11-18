namespace Vora.Application.Auth.External;

public static class AuthRedirectUris
{
    public static string BaseUrl(string? publicBaseUrl, string? emailPublicBaseUrl, string requestOrigin)
    {
        var configured = !string.IsNullOrWhiteSpace(publicBaseUrl) ? publicBaseUrl : emailPublicBaseUrl;
        return (string.IsNullOrWhiteSpace(configured) ? requestOrigin : configured).Trim().TrimEnd('/');
    }

    public static string Callback(string baseUrl, string slug) => $"{baseUrl}/api/auth/oidc/{slug}/callback";
}

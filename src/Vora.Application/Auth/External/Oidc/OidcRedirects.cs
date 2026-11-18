namespace Vora.Application.Auth.External.Oidc;

public static class OidcRedirects
{
    public static string Destination(OidcCallbackResult result)
    {
        var baseUrl = result.BaseUrl.TrimEnd('/');

        return result.Outcome switch
        {
            OidcCallbackOutcome.SignedIn => baseUrl + "/login/external?code=" + Uri.EscapeDataString(result.Code ?? string.Empty) + ReturnTo(result.ReturnTo),
            OidcCallbackOutcome.Pending => baseUrl + "/login/external?pending=" + Uri.EscapeDataString(result.PendingHandle ?? string.Empty),
            OidcCallbackOutcome.Linked => baseUrl + "/account?linked=" + Uri.EscapeDataString(result.Slug),
            _ when result.WasLinkRequest => baseUrl + "/account?linkError=" + Uri.EscapeDataString(result.ErrorCode ?? OidcErrorCodes.ProviderError),
            _ => baseUrl + "/login/external?error=" + Uri.EscapeDataString(result.ErrorCode ?? OidcErrorCodes.ProviderError)
        };
    }

    public static string StartFailure(OidcStartResult result) =>
        result.BaseUrl.TrimEnd('/') + "/login/external?error=" + Uri.EscapeDataString(result.ErrorCode ?? OidcErrorCodes.ProviderUnavailable);

    private static string ReturnTo(string? returnTo) =>
        string.IsNullOrEmpty(returnTo) ? string.Empty : "&returnTo=" + Uri.EscapeDataString(returnTo);
}

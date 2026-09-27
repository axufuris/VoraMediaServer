namespace Vora.Plugins.Providers.OpenSubtitles;

public enum OpenSubtitlesAuthMode
{
    ApiKeyOnly,
    Account,
}

// What the provider decided to do with the configured credentials, and why. The
// "why" matters: account mode with blank credentials is a misconfiguration that
// still works — it downloads against the anonymous quota — so it has to be
// reported rather than either failing hard or passing silently.
public sealed record OpenSubtitlesAuthPlan(
    OpenSubtitlesAuthMode Mode,
    string? Username,
    string? Password,
    string? Warning)
{
    public bool UsesAccount => Mode == OpenSubtitlesAuthMode.Account;

    public const string MissingCredentialsWarning =
        "Authentication mode is set to Account but the username or password is blank. Falling back to API-key-only, which uses the lower anonymous download quota.";

    public const string ApiKeyOnlyLabel = "API key only";
    public const string AccountLabel = "Account (username/password)";

    public static OpenSubtitlesAuthMode ParseMode(string? configured) =>
        configured?.Contains("account", StringComparison.OrdinalIgnoreCase) == true
            ? OpenSubtitlesAuthMode.Account
            : OpenSubtitlesAuthMode.ApiKeyOnly;

    public static OpenSubtitlesAuthPlan Resolve(string? configuredMode, string? username, string? password)
    {
        if (ParseMode(configuredMode) != OpenSubtitlesAuthMode.Account)
        {
            return new OpenSubtitlesAuthPlan(OpenSubtitlesAuthMode.ApiKeyOnly, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new OpenSubtitlesAuthPlan(OpenSubtitlesAuthMode.ApiKeyOnly, null, null, MissingCredentialsWarning);
        }

        return new OpenSubtitlesAuthPlan(OpenSubtitlesAuthMode.Account, username.Trim(), password, null);
    }
}

// A logged-in session: the bearer token and the host the login told us to use.
// Held in memory only — it is re-obtained after a restart, and never written
// anywhere, which is also why neither it nor the password is ever logged.
public sealed record OpenSubtitlesSession(string Token, string BaseUrl, string Username);

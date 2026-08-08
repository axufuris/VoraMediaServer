using System.Net;

namespace Vora.Plugins.Providers.Trakt;

internal static class TraktListResolver
{
    // Turn Trakt's terse status codes into actionable messages. A 403 almost
    // always means the Client ID is wrong (e.g. the Client Secret was pasted
    // instead) rather than anything about the list itself.
    public static void EnsureListResponse(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "Trakt rejected the request (403 Forbidden). This usually means the Trakt Client ID is invalid or the app is unapproved — " +
                "in the plugin settings, enter the application's Client ID (not the Client Secret) from https://trakt.tv/oauth/applications. " +
                "A private list also can't be fetched with a Client ID alone.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "Trakt could not find that list (404). Check the list URL / username-slug is correct and that the list is public.");
        }

        response.EnsureSuccessStatusCode();
    }

    // Turn a user-supplied Trakt list identifier into the API "items" URL.
    // Accepts a full trakt.tv list URL, a "trakt.tv/..." host-relative string,
    // "username/list-slug", or a numeric Trakt list id. A bare non-numeric slug
    // cannot be resolved on its own — Trakt user-list slugs are only unique per
    // owner — so we throw a clear, actionable error instead of a 403.
    public static string BuildItemsUrl(string externalId)
    {
        var value = (externalId ?? string.Empty).Trim();

        var path = value;
        if (value.Contains("://"))
        {
            path = new Uri(value).AbsolutePath;
        }
        else
        {
            var hostIdx = value.IndexOf("trakt.tv", StringComparison.OrdinalIgnoreCase);
            if (hostIdx >= 0) path = value[(hostIdx + "trakt.tv".Length)..];
        }

        var segments = path.Split(new[] { '/', '?' }, StringSplitOptions.RemoveEmptyEntries);

        var usersIdx = Array.FindIndex(segments, s => s.Equals("users", StringComparison.OrdinalIgnoreCase));
        var listsIdx = Array.FindIndex(segments, s => s.Equals("lists", StringComparison.OrdinalIgnoreCase));

        if (usersIdx >= 0 && listsIdx == usersIdx + 2 && segments.Length > listsIdx + 1)
        {
            return UserListItemsUrl(segments[usersIdx + 1], segments[listsIdx + 1]);
        }

        if (listsIdx >= 0 && segments.Length > listsIdx + 1)
        {
            return $"https://api.trakt.tv/lists/{Uri.EscapeDataString(segments[listsIdx + 1])}/items";
        }

        if (segments.Length == 2)
        {
            return UserListItemsUrl(segments[0], segments[1]);
        }

        if (segments.Length == 1 && long.TryParse(segments[0], out _))
        {
            return $"https://api.trakt.tv/lists/{segments[0]}/items";
        }

        throw new InvalidOperationException(
            $"'{externalId}' is a Trakt list slug without its owner. Trakt list slugs aren't unique on their own — " +
            "enter the numeric Trakt list id, use 'username/list-slug', or paste the full Trakt list URL " +
            "(e.g. https://trakt.tv/users/yourname/lists/mcu-complete-chronologically).");
    }

    private static string UserListItemsUrl(string user, string slug)
        => $"https://api.trakt.tv/users/{Uri.EscapeDataString(user)}/lists/{Uri.EscapeDataString(slug)}/items";
}

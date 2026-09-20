using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Tvdb;

public static class TvdbSession
{
    public const string PluginId = "tvdb_metadata";
    public const string TokenKey = "tvdb_token";
    public const string ApiKeyKey = "api_key";
    public const string SubscriberPinKey = "subscriber_pin";

    public static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromDays(1);

    private static readonly SemaphoreSlim LoginGate = new(1, 1);

    // TVDB v4 has two kinds of API key and one login endpoint for both. A
    // licensed (negotiated) key authenticates on its own. A user-supported key —
    // the free tier a self-hoster registers for their own install — additionally
    // needs the PIN from the TVDB subscriber whose account is paying for the
    // lookups, since keys are per-project and individual users no longer get one.
    //
    // The field has to be ABSENT rather than empty for a licensed key: TVDB
    // rejects a login carrying a blank pin, so serializing it unconditionally
    // would break every licensed key the moment the setting existed.
    public static string BuildLoginBody(string apiKey, string? subscriberPin) =>
        string.IsNullOrWhiteSpace(subscriberPin)
            ? JsonSerializer.Serialize(new { apikey = apiKey.Trim() })
            : JsonSerializer.Serialize(new { apikey = apiKey.Trim(), pin = subscriberPin.Trim() });

    public static DateTimeOffset? ReadExpiry(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var parts = token.Split('.');
        if (parts.Length != 3) return null;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static bool NeedsRenewal(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token)) return true;
        var expiry = ReadExpiry(token);
        return expiry.HasValue && expiry.Value - now <= RenewBeforeExpiry;
    }

    public static Task<string?> GetTokenAsync(HttpClient httpClient, IServiceScopeFactory scopeFactory, CancellationToken cancellationToken = default) =>
        GetTokenAsync(httpClient, scopeFactory, null, TimeProvider.System, cancellationToken);

    public static async Task<string?> GetTokenAsync(HttpClient httpClient, IServiceScopeFactory scopeFactory, string? rejectedToken, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        var token = await settings.GetSettingAsync(PluginId, TokenKey);
        if (!IsUnusable(token, rejectedToken, timeProvider)) return token;

        await LoginGate.WaitAsync(cancellationToken);
        try
        {
            token = await settings.GetSettingAsync(PluginId, TokenKey);
            if (!IsUnusable(token, rejectedToken, timeProvider)) return token;

            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(TvdbSession).FullName ?? nameof(TvdbSession));

            var apiKey = await settings.GetSettingAsync(PluginId, ApiKeyKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger?.LogWarning("TVDB has no API key configured, so its session token cannot be renewed.");
                return null;
            }

            var pin = await settings.GetSettingAsync(PluginId, SubscriberPinKey);

            using var content = new StringContent(BuildLoginBody(apiKey, pin), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync("login", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning("TVDB login failed with HTTP {StatusCode}; TVDB lookups will return nothing until it succeeds.", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var renewed = document.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var tokenElement)
                ? tokenElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(renewed))
            {
                logger?.LogWarning("TVDB login succeeded but returned no token.");
                return null;
            }

            await settings.SetSettingAsync(PluginId, TokenKey, renewed);
            logger?.LogInformation("Renewed the TVDB session token; it expires {Expiry}.", ReadExpiry(renewed));
            return renewed;
        }
        finally
        {
            LoginGate.Release();
        }
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient httpClient, IServiceScopeFactory scopeFactory, string url, string token, CancellationToken cancellationToken = default)
    {
        var response = await SendWithTokenAsync(httpClient, url, token, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        var renewed = await GetTokenAsync(httpClient, scopeFactory, token, TimeProvider.System, cancellationToken);
        return renewed == null
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : await SendWithTokenAsync(httpClient, url, renewed, cancellationToken);
    }

    private static bool IsUnusable(string? token, string? rejectedToken, TimeProvider timeProvider) =>
        NeedsRenewal(token, timeProvider.GetUtcNow())
        || (rejectedToken != null && string.Equals(token, rejectedToken, StringComparison.Ordinal));

    private static async Task<HttpResponseMessage> SendWithTokenAsync(HttpClient httpClient, string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await httpClient.SendAsync(request, cancellationToken);
    }
}

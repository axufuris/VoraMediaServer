using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Webhooks;

public interface IMediaWebhookManager
{
    Task<MediaWebhookReceipt> ReceiveAsync(string providerId, string body, IReadOnlyList<string> presentedSecrets);

    Task<List<MediaWebhookProviderVM>> GetProvidersAsync();

    Task<MediaWebhookSecretVM?> GenerateSecretAsync(string providerId);

    IReadOnlyList<MediaWebhookDelivery> GetDeliveries(int limit);
}

public class MediaWebhookManager(
    IEnumerable<IMediaWebhookProvider> providers,
    ISystemSettingsRepository settingsRepo,
    IMediaWebhookQueue queue,
    IMediaWebhookLog log,
    ILogger<MediaWebhookManager> logger) : IMediaWebhookManager
{
    private const string EnabledSettingKey = "is_enabled";
    private const string DisabledValue = "false";

    public async Task<MediaWebhookReceipt> ReceiveAsync(string providerId, string body, IReadOnlyList<string> presentedSecrets)
    {
        var provider = providers.FirstOrDefault(p => p.Id == providerId);
        if (provider == null || await settingsRepo.GetPluginSettingAsync(providerId, EnabledSettingKey) == DisabledValue)
        {
            return new MediaWebhookReceipt(MediaWebhookStatus.NotFound, "Not found.");
        }

        var secret = await settingsRepo.GetPluginSettingAsync(providerId, MediaWebhookSettingKeys.Secret);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return new MediaWebhookReceipt(MediaWebhookStatus.NotFound, "Not found.");
        }

        if (!presentedSecrets.Any(p => SecretsMatch(secret, p)))
        {
            logger.LogWarning("A request to webhook {Provider} carried the wrong secret.", providerId);
            return new MediaWebhookReceipt(MediaWebhookStatus.Unauthorized, "The secret is missing or wrong.");
        }

        var libraryType = LibraryTypeFor(provider);
        if (libraryType == null)
        {
            return new MediaWebhookReceipt(MediaWebhookStatus.BadRequest, "This provider does not name a kind of library.");
        }

        MediaWebhookParseResult parsed;
        try
        {
            parsed = provider.Parse(body);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new MediaWebhookReceipt(MediaWebhookStatus.BadRequest, "The body is not a payload this connection understands.");
        }

        if (parsed.IsTest)
        {
            log.Record(providerId, parsed.InstanceName, "Test", string.Empty, "Test received. Vora is listening.", true);
            return new MediaWebhookReceipt(MediaWebhookStatus.Accepted, "Test received. Vora is listening.");
        }

        if (parsed.Changes.Count == 0)
        {
            log.Record(providerId, parsed.InstanceName, parsed.EventName, string.Empty, "Nothing to do for this event.", true);
            return new MediaWebhookReceipt(MediaWebhookStatus.Accepted, "Nothing to do for this event.");
        }

        queue.Enqueue(new MediaWebhookWork(providerId, parsed.InstanceName, parsed.EventName, libraryType.Value, parsed.Changes));
        return new MediaWebhookReceipt(MediaWebhookStatus.Accepted, $"Received {parsed.Changes.Count} change(s).");
    }

    public async Task<List<MediaWebhookProviderVM>> GetProvidersAsync()
    {
        var result = new List<MediaWebhookProviderVM>();
        foreach (var provider in providers.OrderBy(p => p.Id))
        {
            var secret = await settingsRepo.GetPluginSettingAsync(provider.Id, MediaWebhookSettingKeys.Secret);
            var enabled = await settingsRepo.GetPluginSettingAsync(provider.Id, EnabledSettingKey);
            result.Add(new MediaWebhookProviderVM
            {
                Id = provider.Id,
                Name = provider.Name,
                Description = provider.Description,
                LibraryKind = provider.SupportedLibraryKinds.FirstOrDefault().ToString(),
                ReceivePath = $"/api/webhooks/{provider.Id}",
                IsConfigured = !string.IsNullOrWhiteSpace(secret),
                IsEnabled = enabled != DisabledValue
            });
        }

        return result;
    }

    public async Task<MediaWebhookSecretVM?> GenerateSecretAsync(string providerId)
    {
        if (providers.All(p => p.Id != providerId)) return null;

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await settingsRepo.SetPluginSettingAsync(providerId, MediaWebhookSettingKeys.Secret, secret);
        return new MediaWebhookSecretVM { Secret = secret };
    }

    public IReadOnlyList<MediaWebhookDelivery> GetDeliveries(int limit) => log.Recent(limit);

    private static bool SecretsMatch(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)));

    private static LibraryType? LibraryTypeFor(IMediaWebhookProvider provider) => provider.SupportedLibraryKinds.FirstOrDefault() switch
    {
        LibraryKind.Movie => LibraryType.Movie,
        LibraryKind.TvShow => LibraryType.TvShow,
        LibraryKind.Music => LibraryType.Music,
        LibraryKind.HomeVideo => LibraryType.HomeVideo,
        _ => null
    };
}

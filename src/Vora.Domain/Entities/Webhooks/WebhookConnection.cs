namespace Vora.Domain.Entities.Webhooks;

public class WebhookConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string SecretCiphertext { get; set; } = string.Empty;
    public string PathMappings { get; set; } = string.Empty;
    public bool HandleDeletes { get; set; } = true;
    public string SettingsJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

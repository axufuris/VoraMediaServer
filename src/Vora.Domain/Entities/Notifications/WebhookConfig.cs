using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Notifications;

public class WebhookConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string PayloadUrl { get; set; }

    public List<WebhookEventType> SubscribedEvents { get; set; } = new();
}

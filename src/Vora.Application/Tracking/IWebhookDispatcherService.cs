using Vora.Domain.Enums;

namespace Vora.Application.Tracking;

public interface IWebhookDispatcherService
{
    Task DispatchAsync(WebhookEventType eventType, object payload);
}

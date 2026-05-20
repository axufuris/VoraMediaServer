using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using Vora.Application.Tracking;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Notifications;

public class WebhookDispatcherService : IWebhookDispatcherService
{
    private readonly HttpClient _httpClient;
    private readonly VoraDbContext _context;
    private readonly ILogger<WebhookDispatcherService> _logger;

    public WebhookDispatcherService(HttpClient httpClient, VoraDbContext context, ILogger<WebhookDispatcherService> logger)
    {
        _httpClient = httpClient;
        _context = context;
        _logger = logger;
    }

    public async Task DispatchAsync(WebhookEventType eventType, object payload)
    {
        var subscribedWebhooks = await _context.WebhookConfigs
            .AsNoTracking()
            .Where(w => w.SubscribedEvents.Contains(eventType))
            .ToListAsync();

        if (subscribedWebhooks.Count == 0) return;

        var eventPayload = new
        {
            Event = eventType.ToString(),
            Timestamp = DateTime.UtcNow,
            Data = payload
        };

        var tasks = subscribedWebhooks.Select(async webhook =>
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(webhook.PayloadUrl, eventPayload);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Webhook {WebhookName} failed with status {StatusCode}", webhook.Name, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch webhook to {PayloadUrl}", webhook.PayloadUrl);
            }
        });

        await Task.WhenAll(tasks);
    }
}

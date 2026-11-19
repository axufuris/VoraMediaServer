namespace Vora.Application.Webhooks;

public interface IMediaWebhookLog
{
    void Record(Guid connectionId, string connectionName, string providerId, string? instance, string eventName, string path, string outcome, bool ok);

    IReadOnlyList<MediaWebhookDelivery> Recent(int limit, Guid? connectionId = null);

    MediaWebhookDelivery? Latest(Guid connectionId);
}

public sealed class MediaWebhookLog : IMediaWebhookLog
{
    private const int Capacity = 200;

    private readonly object _gate = new();
    private readonly LinkedList<MediaWebhookDelivery> _entries = new();

    public void Record(Guid connectionId, string connectionName, string providerId, string? instance, string eventName, string path, string outcome, bool ok)
    {
        var entry = new MediaWebhookDelivery(Guid.NewGuid(), DateTime.UtcNow, connectionId, connectionName, providerId, instance, eventName, path, outcome, ok);
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Capacity)
            {
                _entries.RemoveLast();
            }
        }
    }

    public IReadOnlyList<MediaWebhookDelivery> Recent(int limit, Guid? connectionId = null)
    {
        lock (_gate)
        {
            return _entries
                .Where(e => connectionId == null || e.ConnectionId == connectionId)
                .Take(Math.Clamp(limit, 1, Capacity))
                .ToList();
        }
    }

    public MediaWebhookDelivery? Latest(Guid connectionId)
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault(e => e.ConnectionId == connectionId);
        }
    }
}

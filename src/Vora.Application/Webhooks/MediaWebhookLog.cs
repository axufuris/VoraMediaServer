namespace Vora.Application.Webhooks;

public interface IMediaWebhookLog
{
    void Record(string providerId, string? instance, string eventName, string path, string outcome, bool ok);

    IReadOnlyList<MediaWebhookDelivery> Recent(int limit);
}

public sealed class MediaWebhookLog : IMediaWebhookLog
{
    private const int Capacity = 200;

    private readonly object _gate = new();
    private readonly LinkedList<MediaWebhookDelivery> _entries = new();

    public void Record(string providerId, string? instance, string eventName, string path, string outcome, bool ok)
    {
        var entry = new MediaWebhookDelivery(Guid.NewGuid(), DateTime.UtcNow, providerId, instance, eventName, path, outcome, ok);
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Capacity)
            {
                _entries.RemoveLast();
            }
        }
    }

    public IReadOnlyList<MediaWebhookDelivery> Recent(int limit)
    {
        lock (_gate)
        {
            return _entries.Take(Math.Clamp(limit, 1, Capacity)).ToList();
        }
    }
}

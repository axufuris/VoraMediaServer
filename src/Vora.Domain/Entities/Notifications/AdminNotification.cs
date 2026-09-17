using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Notifications;

public class AdminNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AdminNotificationSeverity Severity { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public bool IsRead { get; set; }
    public string? ContextJson { get; set; }
}

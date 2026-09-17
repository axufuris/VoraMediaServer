using Vora.Domain.Entities.Notifications;

namespace Vora.Application.Notifications.ViewModels;

public class AdminNotificationVM
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string? ContextJson { get; set; }

    public static AdminNotificationVM FromEntity(AdminNotification n) => new()
    {
        Id = n.Id,
        CreatedAt = n.CreatedAt,
        Severity = n.Severity.ToString(),
        Title = n.Title,
        Message = n.Message,
        IsRead = n.IsRead,
        ContextJson = n.ContextJson
    };
}

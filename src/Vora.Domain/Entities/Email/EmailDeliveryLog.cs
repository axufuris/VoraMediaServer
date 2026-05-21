using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Email;

public class EmailDeliveryLog
{
    public Guid Id { get; set; }
    public EmailTemplateKey TemplateKey { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public EmailDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}

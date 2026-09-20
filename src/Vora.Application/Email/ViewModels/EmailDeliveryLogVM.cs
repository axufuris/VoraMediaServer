using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Application.Email.ViewModels;

public class EmailDeliveryLogVM
{
    public Guid Id { get; set; }
    public EmailTemplateKey TemplateKey { get; set; }
    public required string ToAddress { get; set; }
    public required string Subject { get; set; }
    public EmailDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    public static EmailDeliveryLogVM From(EmailDeliveryLog log) => new()
    {
        Id = log.Id,
        TemplateKey = log.TemplateKey,
        ToAddress = log.ToAddress,
        Subject = log.Subject,
        Status = log.Status,
        AttemptCount = log.AttemptCount,
        ErrorMessage = Truncate(log.ErrorMessage, 512),
        CreatedAt = log.CreatedAt,
        SentAt = log.SentAt
    };

    private static string? Truncate(string? value, int max) =>
        value is null ? null : (value.Length <= max ? value : value[..max] + "…");
}

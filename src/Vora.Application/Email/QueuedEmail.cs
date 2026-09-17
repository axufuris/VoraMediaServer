using Vora.Domain.Enums;

namespace Vora.Application.Email;

public class QueuedEmail
{
    public required Guid LogId { get; init; }
    public required EmailTemplateKey TemplateKey { get; init; }
    public required string ToAddress { get; init; }
    public string? ToDisplayName { get; init; }
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public required string TextBody { get; init; }
}

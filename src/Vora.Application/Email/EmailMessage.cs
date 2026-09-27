using Vora.Domain.Enums;

namespace Vora.Application.Email;

public class EmailMessage
{
    public required EmailTemplateKey TemplateKey { get; init; }
    public required string ToAddress { get; init; }
    public string? ToDisplayName { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
}

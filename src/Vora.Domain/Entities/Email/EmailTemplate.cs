using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Email;

public class EmailTemplate
{
    public EmailTemplateKey Key { get; set; }
    public string? SubjectOverride { get; set; }
    public string? HtmlBodyOverride { get; set; }
    public string? TextBodyOverride { get; set; }
    public DateTime UpdatedAt { get; set; }
}

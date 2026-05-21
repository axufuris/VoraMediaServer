using Vora.Domain.Enums;

namespace Vora.Application.Email.ViewModels;

public class EmailTemplateVariableVM
{
    public required string Name { get; set; }
    public required string Description { get; set; }
}

public class EmailTemplateDetailVM
{
    public EmailTemplateKey Key { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public required string DefaultSubject { get; set; }
    public required string DefaultHtmlBody { get; set; }
    public required string DefaultTextBody { get; set; }
    public string? SubjectOverride { get; set; }
    public string? HtmlBodyOverride { get; set; }
    public string? TextBodyOverride { get; set; }
    public bool HasOverride { get; set; }
    public DateTime? OverrideUpdatedAt { get; set; }
    public required IReadOnlyList<EmailTemplateVariableVM> AvailableVariables { get; set; }
}

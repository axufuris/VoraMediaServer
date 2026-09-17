namespace Vora.Application.Email.ViewModels;

public class UpdateEmailTemplateRequest
{
    public string? SubjectOverride { get; set; }
    public string? HtmlBodyOverride { get; set; }
    public string? TextBodyOverride { get; set; }
}

namespace Vora.Application.Email.ViewModels;

public class UpdateEmailSettingsRequest
{
    public bool EmailEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseStartTls { get; set; } = true;
    public bool SmtpUseImplicitSsl { get; set; }
    public string? SmtpUsername { get; set; }
    public string? NewSmtpPassword { get; set; }
    public bool ClearSmtpPassword { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromDisplayName { get; set; }
    public string? EmailPublicBaseUrl { get; set; }
}

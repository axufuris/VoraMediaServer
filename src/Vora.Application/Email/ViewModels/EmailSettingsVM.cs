using System.Linq.Expressions;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Email.ViewModels;

public class EmailSettingsVM
{
    public bool EmailEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseStartTls { get; set; } = true;
    public bool SmtpUseImplicitSsl { get; set; }
    public string? SmtpUsername { get; set; }
    public bool SmtpPasswordIsSet { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromDisplayName { get; set; }
    public string? EmailPublicBaseUrl { get; set; }

    public static Expression<Func<ServerSetting, EmailSettingsVM>> Projection =>
        s => new EmailSettingsVM
        {
            EmailEnabled = s.EmailEnabled,
            SmtpHost = s.SmtpHost,
            SmtpPort = s.SmtpPort,
            SmtpUseStartTls = s.SmtpUseStartTls,
            SmtpUseImplicitSsl = s.SmtpUseImplicitSsl,
            SmtpUsername = s.SmtpUsername,
            SmtpPasswordIsSet = !string.IsNullOrEmpty(s.SmtpPasswordCiphertext),
            SmtpFromAddress = s.SmtpFromAddress,
            SmtpFromDisplayName = s.SmtpFromDisplayName,
            EmailPublicBaseUrl = s.EmailPublicBaseUrl
        };
}

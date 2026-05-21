using Vora.Application.Email.ViewModels;
using Vora.Application.Settings;
using Vora.Domain.Enums;

namespace Vora.Application.Email;

public interface IEmailSettingsManager
{
    Task<EmailSettingsVM> GetSettingsAsync();
    Task UpdateSettingsAsync(UpdateEmailSettingsRequest request);
    Task<SendTestEmailResponse> SendTestAsync(string toAddress, CancellationToken cancellationToken = default);
}

public class EmailSettingsManager : IEmailSettingsManager
{
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IEmailSecretProtector _secretProtector;
    private readonly IEmailService _emailService;

    public EmailSettingsManager(
        ISystemSettingsRepository settingsRepo,
        IEmailSecretProtector secretProtector,
        IEmailService emailService)
    {
        _settingsRepo = settingsRepo;
        _secretProtector = secretProtector;
        _emailService = emailService;
    }

    public async Task<EmailSettingsVM> GetSettingsAsync()
    {
        await _settingsRepo.GetSettingsForUpdateAsync();
        var settings = await _settingsRepo.GetSettingsAsync();
        return new EmailSettingsVM
        {
            EmailEnabled = settings.EmailEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort,
            SmtpUseStartTls = settings.SmtpUseStartTls,
            SmtpUseImplicitSsl = settings.SmtpUseImplicitSsl,
            SmtpUsername = settings.SmtpUsername,
            SmtpPasswordIsSet = !string.IsNullOrEmpty(settings.SmtpPasswordCiphertext),
            SmtpFromAddress = settings.SmtpFromAddress,
            SmtpFromDisplayName = settings.SmtpFromDisplayName,
            EmailPublicBaseUrl = settings.EmailPublicBaseUrl
        };
    }

    public async Task UpdateSettingsAsync(UpdateEmailSettingsRequest request)
    {
        var settings = await _settingsRepo.GetSettingsForUpdateAsync();

        settings.EmailEnabled = request.EmailEnabled;
        settings.SmtpHost = NormalizeOrNull(request.SmtpHost);
        settings.SmtpPort = ClampPort(request.SmtpPort);
        settings.SmtpUseStartTls = request.SmtpUseStartTls;
        settings.SmtpUseImplicitSsl = request.SmtpUseImplicitSsl;
        settings.SmtpUsername = NormalizeOrNull(request.SmtpUsername);
        settings.SmtpFromAddress = NormalizeOrNull(request.SmtpFromAddress);
        settings.SmtpFromDisplayName = NormalizeOrNull(request.SmtpFromDisplayName);
        settings.EmailPublicBaseUrl = NormalizeOrNull(request.EmailPublicBaseUrl);

        if (request.ClearSmtpPassword)
        {
            settings.SmtpPasswordCiphertext = null;
        }
        else if (!string.IsNullOrEmpty(request.NewSmtpPassword))
        {
            settings.SmtpPasswordCiphertext = _secretProtector.Protect(request.NewSmtpPassword);
        }

        await _settingsRepo.SaveChangesAsync();
    }

    public async Task<SendTestEmailResponse> SendTestAsync(string toAddress, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync();

        var message = new EmailMessage
        {
            TemplateKey = EmailTemplateKey.TestEmail,
            ToAddress = toAddress,
            Variables = new Dictionary<string, string>
            {
                [EmailTemplateVariables.ServerName] = string.IsNullOrWhiteSpace(settings.ServerName) ? "Vora" : settings.ServerName
            }
        };

        var result = await _emailService.SendImmediateAsync(message, cancellationToken);
        return result.Outcome switch
        {
            EmailSendOutcome.Sent => new SendTestEmailResponse { Success = true, Message = "Test email sent." },
            EmailSendOutcome.Skipped => new SendTestEmailResponse { Success = false, Message = result.ErrorMessage ?? "Skipped." },
            _ => new SendTestEmailResponse { Success = false, Message = result.ErrorMessage ?? "Failed to send test email." }
        };
    }

    private static string? NormalizeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ClampPort(int port) => Math.Clamp(port, 1, 65535);
}

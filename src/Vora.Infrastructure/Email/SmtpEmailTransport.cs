using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Vora.Application.Email;
using Vora.Application.Settings;

namespace Vora.Infrastructure.Email;

public class SmtpEmailTransport : IEmailTransport
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IEmailSecretProtector _secretProtector;

    public SmtpEmailTransport(ISystemSettingsRepository settingsRepo, IEmailSecretProtector secretProtector)
    {
        _settingsRepo = settingsRepo;
        _secretProtector = secretProtector;
    }

    public async Task SendAsync(QueuedEmail email, CancellationToken cancellationToken)
    {
        var settings = await _settingsRepo.GetSettingsAsync();

        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            throw new InvalidOperationException("SMTP host is not configured.");
        }
        if (string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
        {
            throw new InvalidOperationException("SMTP from address is not configured.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.SmtpFromDisplayName ?? string.Empty, settings.SmtpFromAddress));
        message.To.Add(new MailboxAddress(email.ToDisplayName ?? string.Empty, email.ToAddress));
        message.Subject = email.Subject;

        var builder = new BodyBuilder
        {
            HtmlBody = email.HtmlBody,
            TextBody = email.TextBody
        };
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient
        {
            Timeout = (int)ConnectTimeout.TotalMilliseconds
        };

        var socketOptions = ResolveSocketOptions(settings.SmtpUseImplicitSsl, settings.SmtpUseStartTls);

        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, socketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername) && !string.IsNullOrWhiteSpace(settings.SmtpPasswordCiphertext))
        {
            var password = _secretProtector.Unprotect(settings.SmtpPasswordCiphertext);
            await client.AuthenticateAsync(settings.SmtpUsername, password, cancellationToken);
        }

        try
        {
            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            await client.DisconnectAsync(true, cancellationToken);
        }
    }

    private static SecureSocketOptions ResolveSocketOptions(bool useImplicitSsl, bool useStartTls)
    {
        if (useImplicitSsl) return SecureSocketOptions.SslOnConnect;
        if (useStartTls) return SecureSocketOptions.StartTls;
        return SecureSocketOptions.None;
    }
}

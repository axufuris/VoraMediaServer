using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Application.Email;

public interface IEmailService
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    Task<EmailSendResult> SendImmediateAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public class EmailService : IEmailService
{
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly IEmailDispatchQueue _queue;
    private readonly IEmailTransport _transport;
    private readonly IEmailDeliveryLogRepository _logRepo;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        ISystemSettingsRepository settingsRepo,
        IEmailTemplateRenderer renderer,
        IEmailDispatchQueue queue,
        IEmailTransport transport,
        IEmailDeliveryLogRepository logRepo,
        ILogger<EmailService> logger)
    {
        _settingsRepo = settingsRepo;
        _renderer = renderer;
        _queue = queue;
        _transport = transport;
        _logRepo = logRepo;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(message, requireEnabled: true, cancellationToken);
        if (prepared.SkipReason is not null)
        {
            return EmailSendResult.Skipped(prepared.SkipReason);
        }

        var queued = prepared.Queued!;
        await _queue.EnqueueAsync(queued, cancellationToken);
        return EmailSendResult.Queued(queued.LogId);
    }

    public async Task<EmailSendResult> SendImmediateAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(message, requireEnabled: false, cancellationToken);
        if (prepared.SkipReason is not null)
        {
            return EmailSendResult.Skipped(prepared.SkipReason);
        }

        var queued = prepared.Queued!;

        try
        {
            await _transport.SendAsync(queued, cancellationToken);
            await _logRepo.UpdateAsync(queued.LogId, EmailDeliveryStatus.Sent, attemptCount: 1, errorMessage: null, sentAt: DateTime.UtcNow, cancellationToken);
            return EmailSendResult.Sent(queued.LogId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Immediate email send failed for template {Template} to {To}", queued.TemplateKey, queued.ToAddress);
            await _logRepo.UpdateAsync(queued.LogId, EmailDeliveryStatus.Failed, attemptCount: 1, errorMessage: ex.Message, sentAt: null, cancellationToken);
            return EmailSendResult.Failed(ex.Message, queued.LogId);
        }
    }

    private async Task<PreparedEmail> PrepareAsync(EmailMessage message, bool requireEnabled, CancellationToken cancellationToken)
    {
        var settings = await _settingsRepo.GetSettingsAsync();

        if (requireEnabled && !settings.EmailEnabled)
        {
            return PreparedEmail.Skip("Email is disabled.");
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
        {
            return PreparedEmail.Skip("Email transport is not configured.");
        }

        if (!IsValidAddress(message.ToAddress))
        {
            return PreparedEmail.Skip("Recipient address is invalid.");
        }

        var rendered = await _renderer.RenderAsync(message.TemplateKey, message.Variables, cancellationToken);

        var logRow = await _logRepo.CreateAsync(new EmailDeliveryLog
        {
            Id = Guid.NewGuid(),
            TemplateKey = message.TemplateKey,
            ToAddress = message.ToAddress,
            Subject = rendered.Subject,
            Status = EmailDeliveryStatus.Queued,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        return PreparedEmail.Ready(new QueuedEmail
        {
            LogId = logRow.Id,
            TemplateKey = message.TemplateKey,
            ToAddress = message.ToAddress,
            ToDisplayName = message.ToDisplayName,
            Subject = rendered.Subject,
            HtmlBody = rendered.HtmlBody,
            TextBody = rendered.TextBody
        });
    }

    private static bool IsValidAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        if (address.Contains('\r') || address.Contains('\n')) return false;
        var at = address.IndexOf('@');
        return at > 0 && at < address.Length - 1;
    }

    private sealed class PreparedEmail
    {
        public QueuedEmail? Queued { get; private init; }
        public string? SkipReason { get; private init; }

        public static PreparedEmail Ready(QueuedEmail queued) => new() { Queued = queued };
        public static PreparedEmail Skip(string reason) => new() { SkipReason = reason };
    }
}

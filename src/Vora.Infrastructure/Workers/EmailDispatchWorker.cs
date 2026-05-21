using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Email;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Workers;

public class EmailDispatchWorker : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30)
    };

    private readonly IEmailDispatchQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDispatchWorker> _logger;

    public EmailDispatchWorker(
        IEmailDispatchQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailDispatchWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email Dispatch Worker started.");

        try
        {
            await foreach (var email in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await DispatchAsync(email, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Email Dispatch Worker stopping.");
        }
    }

    private async Task DispatchAsync(QueuedEmail email, CancellationToken stoppingToken)
    {
        var attempts = RetryDelays.Length + 1;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
                var logRepo = scope.ServiceProvider.GetRequiredService<IEmailDeliveryLogRepository>();

                await transport.SendAsync(email, stoppingToken);
                await logRepo.UpdateAsync(email.LogId, EmailDeliveryStatus.Sent, attempt, errorMessage: null, sentAt: DateTime.UtcNow, stoppingToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Email send attempt {Attempt}/{Max} failed for {Template} to {To}", attempt, attempts, email.TemplateKey, email.ToAddress);

                if (attempt >= attempts) break;

                try
                {
                    await Task.Delay(RetryDelays[attempt - 1], stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var logRepo = scope.ServiceProvider.GetRequiredService<IEmailDeliveryLogRepository>();
            await logRepo.UpdateAsync(email.LogId, EmailDeliveryStatus.Failed, attempts, lastError?.Message, sentAt: null, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist final email delivery failure for {LogId}", email.LogId);
        }
    }
}

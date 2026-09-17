using Microsoft.Extensions.Logging;
using Vora.Application.Email;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Enums;

namespace Vora.Application.Requests;

public interface IRequestNotificationService
{
    Task NotifyRequestAvailableAsync(MediaRequest request, Guid? mediaItemId, CancellationToken cancellationToken = default);
}

public class RequestNotificationService : IRequestNotificationService
{
    private readonly IUserRepository _userRepo;
    private readonly IRequestRepository _requestRepo;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IEmailService _emailService;
    private readonly ILogger<RequestNotificationService> _logger;

    public RequestNotificationService(
        IUserRepository userRepo,
        IRequestRepository requestRepo,
        ISystemSettingsRepository settingsRepo,
        IEmailService emailService,
        ILogger<RequestNotificationService> logger)
    {
        _userRepo = userRepo;
        _requestRepo = requestRepo;
        _settingsRepo = settingsRepo;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task NotifyRequestAvailableAsync(MediaRequest request, Guid? mediaItemId, CancellationToken cancellationToken = default)
    {
        if (request.Requesters.Count == 0) return;

        var pending = request.Requesters.Where(r => r.NotifiedAt is null).ToList();
        if (pending.Count == 0) return;

        var settings = await _settingsRepo.GetSettingsAsync();
        if (!settings.EmailEnabled)
        {
            return;
        }

        var serverName = string.IsNullOrWhiteSpace(settings.ServerName) ? "Vora" : settings.ServerName;
        var mediaLink = BuildMediaLink(settings.EmailPublicBaseUrl, mediaItemId, request.ExternalId);
        var anyChanged = false;

        foreach (var requester in pending)
        {
            try
            {
                var user = await _userRepo.GetUserForProfileAsync(requester.ProfileId);
                if (user is null) continue;
                if (string.IsNullOrWhiteSpace(user.Email)) continue;
                if (!user.EmailNotifyOnRequestAvailable) continue;

                var message = new EmailMessage
                {
                    TemplateKey = EmailTemplateKey.RequestAvailable,
                    ToAddress = user.Email,
                    ToDisplayName = user.DisplayName,
                    Variables = new Dictionary<string, string>
                    {
                        [EmailTemplateVariables.UserName] = user.DisplayName,
                        [EmailTemplateVariables.MediaTitle] = request.Title,
                        [EmailTemplateVariables.MediaType] = request.Type,
                        [EmailTemplateVariables.MediaLink] = mediaLink,
                        [EmailTemplateVariables.PosterUrl] = request.PosterUrl ?? string.Empty,
                        [EmailTemplateVariables.ServerName] = serverName
                    }
                };

                await _emailService.SendAsync(message, cancellationToken);
                requester.NotifiedAt = DateTime.UtcNow;
                anyChanged = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue request-available email for profile {ProfileId} request {RequestId}", requester.ProfileId, request.Id);
            }
        }

        if (anyChanged)
        {
            try
            {
                await _requestRepo.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist NotifiedAt for request {RequestId}", request.Id);
            }
        }
    }

    private static string BuildMediaLink(string? configuredBaseUrl, Guid? mediaItemId, string externalId)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? string.Empty : configuredBaseUrl.TrimEnd('/');

        if (mediaItemId.HasValue)
        {
            return $"{baseUrl}/media/{mediaItemId.Value}";
        }

        return $"{baseUrl}/search?q={Uri.EscapeDataString(externalId)}";
    }
}

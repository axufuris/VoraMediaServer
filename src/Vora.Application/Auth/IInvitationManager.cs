using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Vora.Application.Auth.ViewModels;
using Vora.Application.Email;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Auth;

public enum InvitationCreateOutcome
{
    Created,
    EmailDisabled,
    EmailAlreadyRegistered
}

public class InvitationCreateResult
{
    public required InvitationCreateOutcome Outcome { get; init; }
    public InvitationVM? Invitation { get; init; }
    public string? PlaintextToken { get; init; }
    public bool EmailSent { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface IInvitationManager
{
    Task<InvitationCreateResult> CreateInvitationAsync(string email, int? expiresInDays, Guid? invitedByUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvitationVM>> GetActiveInvitationsAsync();
    Task<InvitationTicket?> ValidateTokenAsync(string token);
    Task<bool> RevokeAsync(Guid id);
    Task ConsumeAsync(string tokenHash);
}

public class InvitationManager : IInvitationManager
{
    private const int DefaultExpiresInDays = 7;
    private const int MaxExpiresInDays = 60;

    private readonly IInvitationRepository _invitationRepo;
    private readonly IUserRepository _userRepo;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IEmailService _emailService;
    private readonly ILogger<InvitationManager> _logger;

    public InvitationManager(
        IInvitationRepository invitationRepo,
        IUserRepository userRepo,
        ISystemSettingsRepository settingsRepo,
        IEmailService emailService,
        ILogger<InvitationManager> logger)
    {
        _invitationRepo = invitationRepo;
        _userRepo = userRepo;
        _settingsRepo = settingsRepo;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<InvitationCreateResult> CreateInvitationAsync(string email, int? expiresInDays, Guid? invitedByUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new InvitationCreateResult
            {
                Outcome = InvitationCreateOutcome.EmailDisabled,
                ErrorMessage = "Email address is required."
            };
        }

        var settings = await _settingsRepo.GetSettingsAsync();
        if (!settings.EmailEnabled)
        {
            return new InvitationCreateResult
            {
                Outcome = InvitationCreateOutcome.EmailDisabled,
                ErrorMessage = "Email must be enabled in server settings before invitations can be sent."
            };
        }

        var normalized = email.Trim().ToLowerInvariant();

        var existingUser = await _userRepo.GetUserWithProfilesByEmailAsync(normalized);
        if (existingUser is not null)
        {
            return new InvitationCreateResult
            {
                Outcome = InvitationCreateOutcome.EmailAlreadyRegistered,
                ErrorMessage = "An account already exists for that email."
            };
        }

        await _invitationRepo.InvalidateOutstandingForEmailAsync(normalized);

        var days = Math.Clamp(expiresInDays ?? DefaultExpiresInDays, 1, MaxExpiresInDays);
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        var ticket = new InvitationTicket
        {
            Email = normalized,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(days),
            InvitedByUserId = invitedByUserId
        };

        await _invitationRepo.CreateAsync(ticket);

        var inviteLink = BuildInviteLink(settings.EmailPublicBaseUrl, token);
        var serverName = string.IsNullOrWhiteSpace(settings.ServerName) ? "Vora" : settings.ServerName;
        var emailSent = false;
        string? errorMessage = null;

        try
        {
            var result = await _emailService.SendAsync(new EmailMessage
            {
                TemplateKey = EmailTemplateKey.AdminInvite,
                ToAddress = normalized,
                Variables = new Dictionary<string, string>
                {
                    [EmailTemplateVariables.InviteLink] = inviteLink,
                    [EmailTemplateVariables.InviteEmail] = normalized,
                    [EmailTemplateVariables.ServerName] = serverName
                }
            }, cancellationToken);

            emailSent = result.Outcome is EmailSendOutcome.Queued or EmailSendOutcome.Sent;
            if (!emailSent)
            {
                errorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue invitation email for {Email}", normalized);
            errorMessage = ex.Message;
        }

        return new InvitationCreateResult
        {
            Outcome = InvitationCreateOutcome.Created,
            Invitation = ToVM(ticket),
            PlaintextToken = token,
            EmailSent = emailSent,
            ErrorMessage = errorMessage
        };
    }

    public async Task<IReadOnlyList<InvitationVM>> GetActiveInvitationsAsync()
    {
        var rows = await _invitationRepo.GetAllActiveAsync();
        return rows.Select(ToVM).ToList();
    }

    public async Task<InvitationTicket?> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = HashToken(token);
        return await _invitationRepo.GetByTokenHashAsync(hash);
    }

    public Task<bool> RevokeAsync(Guid id) => _invitationRepo.DeleteAsync(id);

    public Task ConsumeAsync(string tokenHash) => _invitationRepo.DeleteByTokenHashAsync(tokenHash);

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string BuildInviteLink(string? configuredBaseUrl, string token)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? string.Empty : configuredBaseUrl.TrimEnd('/');
        return $"{baseUrl}/register?invite={Uri.EscapeDataString(token)}";
    }

    private static InvitationVM ToVM(InvitationTicket ticket) => new()
    {
        Id = ticket.Id,
        Email = ticket.Email,
        CreatedAt = ticket.CreatedAt,
        ExpiresAt = ticket.ExpiresAt,
        InvitedByUserId = ticket.InvitedByUserId
    };
}

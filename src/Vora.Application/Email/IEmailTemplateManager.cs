using Vora.Application.Email.ViewModels;
using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Application.Email;

public interface IEmailTemplateManager
{
    Task<IReadOnlyList<EmailTemplateSummaryVM>> ListAsync(CancellationToken cancellationToken = default);
    Task<EmailTemplateDetailVM?> GetAsync(EmailTemplateKey key, CancellationToken cancellationToken = default);
    Task UpdateAsync(EmailTemplateKey key, UpdateEmailTemplateRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(EmailTemplateKey key, CancellationToken cancellationToken = default);
}

public class EmailTemplateManager : IEmailTemplateManager
{
    private static readonly IReadOnlyDictionary<EmailTemplateKey, (string DisplayName, string Description)> Metadata =
        new Dictionary<EmailTemplateKey, (string, string)>
        {
            [EmailTemplateKey.PasswordReset] = ("Password reset", "Sent when a user requests a password reset link."),
            [EmailTemplateKey.AdminInvite] = ("Admin invitation", "Sent when an admin invites a new user to register."),
            [EmailTemplateKey.RequestAvailable] = ("Request available", "Sent when a previously requested media item lands in the library."),
            [EmailTemplateKey.TestEmail] = ("Test email", "Sent from the admin email settings test button.")
        };

    private readonly IEmailTemplateRepository _templateRepo;
    private readonly IEmailTemplateRenderer _renderer;

    public EmailTemplateManager(IEmailTemplateRepository templateRepo, IEmailTemplateRenderer renderer)
    {
        _templateRepo = templateRepo;
        _renderer = renderer;
    }

    public async Task<IReadOnlyList<EmailTemplateSummaryVM>> ListAsync(CancellationToken cancellationToken = default)
    {
        var overrides = await _templateRepo.GetAllOverridesAsync(cancellationToken);
        var overrideMap = overrides.ToDictionary(o => o.Key);

        var result = new List<EmailTemplateSummaryVM>();
        foreach (var kvp in Metadata)
        {
            overrideMap.TryGetValue(kvp.Key, out var existing);
            result.Add(new EmailTemplateSummaryVM
            {
                Key = kvp.Key,
                DisplayName = kvp.Value.DisplayName,
                Description = kvp.Value.Description,
                HasOverride = HasAnyOverride(existing),
                OverrideUpdatedAt = existing?.UpdatedAt
            });
        }
        return result;
    }

    public async Task<EmailTemplateDetailVM?> GetAsync(EmailTemplateKey key, CancellationToken cancellationToken = default)
    {
        if (!Metadata.TryGetValue(key, out var meta)) return null;

        var builtIn = await _renderer.GetBuiltInAsync(key, cancellationToken);
        var overrideRow = await _templateRepo.GetOverrideAsync(key, cancellationToken);

        var variables = EmailTemplateVariables.For(key)
            .Select(v => new EmailTemplateVariableVM { Name = v.Name, Description = v.Description })
            .ToList();

        return new EmailTemplateDetailVM
        {
            Key = key,
            DisplayName = meta.DisplayName,
            Description = meta.Description,
            DefaultSubject = builtIn.Subject,
            DefaultHtmlBody = builtIn.HtmlBody,
            DefaultTextBody = builtIn.TextBody,
            SubjectOverride = overrideRow?.SubjectOverride,
            HtmlBodyOverride = overrideRow?.HtmlBodyOverride,
            TextBodyOverride = overrideRow?.TextBodyOverride,
            HasOverride = HasAnyOverride(overrideRow),
            OverrideUpdatedAt = overrideRow?.UpdatedAt,
            AvailableVariables = variables
        };
    }

    public Task UpdateAsync(EmailTemplateKey key, UpdateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (!Metadata.ContainsKey(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Unknown email template key: {key}");
        }

        var template = new EmailTemplate
        {
            Key = key,
            SubjectOverride = NormalizeOrNull(request.SubjectOverride),
            HtmlBodyOverride = NormalizeOrNull(request.HtmlBodyOverride),
            TextBodyOverride = NormalizeOrNull(request.TextBodyOverride)
        };

        return _templateRepo.UpsertOverrideAsync(template, cancellationToken);
    }

    public Task DeleteAsync(EmailTemplateKey key, CancellationToken cancellationToken = default) =>
        _templateRepo.DeleteOverrideAsync(key, cancellationToken);

    private static bool HasAnyOverride(EmailTemplate? row) =>
        row is not null &&
        (!string.IsNullOrWhiteSpace(row.SubjectOverride) ||
         !string.IsNullOrWhiteSpace(row.HtmlBodyOverride) ||
         !string.IsNullOrWhiteSpace(row.TextBodyOverride));

    private static string? NormalizeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

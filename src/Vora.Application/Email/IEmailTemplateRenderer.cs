using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Application.Email;

public interface IEmailTemplateRenderer
{
    Task<RenderedEmail> RenderAsync(EmailTemplateKey key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default);
    Task<EmailTemplateContent> GetBuiltInAsync(EmailTemplateKey key, CancellationToken cancellationToken = default);
}

public class EmailTemplateContent
{
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public required string TextBody { get; init; }
}

public class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly Regex VariablePattern = new(@"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}", RegexOptions.Compiled);
    private static readonly Assembly ResourceAssembly = typeof(EmailTemplateRenderer).Assembly;
    private const string ResourcePrefix = "Vora.Application.Email.Templates.";

    private readonly IEmailTemplateRepository _templateRepo;

    public EmailTemplateRenderer(IEmailTemplateRepository templateRepo)
    {
        _templateRepo = templateRepo;
    }

    public async Task<RenderedEmail> RenderAsync(EmailTemplateKey key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default)
    {
        var content = await ResolveContentAsync(key, cancellationToken);

        return new RenderedEmail
        {
            Subject = SanitizeSubject(Substitute(content.Subject, variables, encode: false)),
            HtmlBody = Substitute(content.HtmlBody, variables, encode: true),
            TextBody = Substitute(content.TextBody, variables, encode: false)
        };
    }

    public Task<EmailTemplateContent> GetBuiltInAsync(EmailTemplateKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(LoadBuiltIn(key));

    private async Task<EmailTemplateContent> ResolveContentAsync(EmailTemplateKey key, CancellationToken cancellationToken)
    {
        var builtIn = LoadBuiltIn(key);
        var overrideRow = await _templateRepo.GetOverrideAsync(key, cancellationToken);
        if (overrideRow is null)
        {
            return builtIn;
        }

        return new EmailTemplateContent
        {
            Subject = NotBlank(overrideRow.SubjectOverride) ?? builtIn.Subject,
            HtmlBody = NotBlank(overrideRow.HtmlBodyOverride) ?? builtIn.HtmlBody,
            TextBody = NotBlank(overrideRow.TextBodyOverride) ?? builtIn.TextBody
        };
    }

    private static string? NotBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static EmailTemplateContent LoadBuiltIn(EmailTemplateKey key)
    {
        var baseName = key.ToString();
        return new EmailTemplateContent
        {
            Subject = ReadResource($"{baseName}.subject.txt").Trim(),
            HtmlBody = ReadResource($"{baseName}.html"),
            TextBody = ReadResource($"{baseName}.txt")
        };
    }

    private static string ReadResource(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Built-in email template resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Substitute(string template, IReadOnlyDictionary<string, string> variables, bool encode) =>
        VariablePattern.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!variables.TryGetValue(name, out var value)) return string.Empty;
            return encode ? WebUtility.HtmlEncode(value) : value;
        });

    private static string SanitizeSubject(string subject)
    {
        var trimmed = subject.Replace("\r", string.Empty).Replace("\n", " ").Trim();
        return trimmed.Length > 256 ? trimmed[..256] : trimmed;
    }
}

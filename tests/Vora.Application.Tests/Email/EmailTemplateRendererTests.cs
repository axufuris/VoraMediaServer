using Vora.Application.Email;
using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Email;

public class EmailTemplateRendererTests
{
    private readonly IEmailTemplateRepository _repo;
    private readonly EmailTemplateRenderer _renderer;

    public EmailTemplateRendererTests()
    {
        _repo = Substitute.For<IEmailTemplateRepository>();
        _renderer = new EmailTemplateRenderer(_repo);
    }

    [Fact]
    public async Task RenderAsync_substitutes_variables_in_password_reset_template()
    {
        _repo.GetOverrideAsync(EmailTemplateKey.PasswordReset, Arg.Any<CancellationToken>())
            .Returns((EmailTemplate?)null);

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.PasswordReset, new Dictionary<string, string>
        {
            ["serverName"] = "Vora-One",
            ["userName"] = "Andy",
            ["resetLink"] = "https://example.com/reset?token=abc"
        });

        rendered.Subject.Should().Contain("Vora-One");
        rendered.TextBody.Should().Contain("Andy");
        rendered.TextBody.Should().Contain("https://example.com/reset?token=abc");
        rendered.HtmlBody.Should().Contain("Andy");
    }

    [Fact]
    public async Task RenderAsync_drops_unknown_variables_to_empty_string()
    {
        _repo.GetOverrideAsync(Arg.Any<EmailTemplateKey>(), Arg.Any<CancellationToken>())
            .Returns((EmailTemplate?)null);

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.PasswordReset, new Dictionary<string, string>
        {
            ["serverName"] = "Vora-One"
        });

        rendered.TextBody.Should().NotContain("{{userName}}");
        rendered.TextBody.Should().NotContain("{{resetLink}}");
    }

    [Fact]
    public async Task RenderAsync_html_encodes_user_supplied_values_only_in_html_body()
    {
        _repo.GetOverrideAsync(Arg.Any<EmailTemplateKey>(), Arg.Any<CancellationToken>())
            .Returns((EmailTemplate?)null);

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.PasswordReset, new Dictionary<string, string>
        {
            ["serverName"] = "Vora",
            ["userName"] = "<script>alert('xss')</script>",
            ["resetLink"] = "https://example.com/reset"
        });

        rendered.HtmlBody.Should().Contain("&lt;script&gt;");
        rendered.HtmlBody.Should().NotContain("<script>");
        rendered.TextBody.Should().Contain("<script>alert('xss')</script>");
    }

    [Fact]
    public async Task RenderAsync_subject_strips_newlines_and_carriage_returns()
    {
        _repo.GetOverrideAsync(EmailTemplateKey.AdminInvite, Arg.Any<CancellationToken>())
            .Returns(new EmailTemplate
            {
                Key = EmailTemplateKey.AdminInvite,
                SubjectOverride = "Hello\r\nNew{{userName}}\r\nLine",
                HtmlBodyOverride = "<p>body</p>",
                TextBodyOverride = "body"
            });

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.AdminInvite, new Dictionary<string, string>
        {
            ["userName"] = "Andy"
        });

        rendered.Subject.Should().NotContain("\r");
        rendered.Subject.Should().NotContain("\n");
        rendered.Subject.Should().Contain("Andy");
    }

    [Fact]
    public async Task RenderAsync_subject_truncated_to_256_chars()
    {
        var longSubject = new string('A', 400);
        _repo.GetOverrideAsync(EmailTemplateKey.AdminInvite, Arg.Any<CancellationToken>())
            .Returns(new EmailTemplate
            {
                Key = EmailTemplateKey.AdminInvite,
                SubjectOverride = longSubject,
                HtmlBodyOverride = "<p>body</p>",
                TextBodyOverride = "body"
            });

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.AdminInvite, new Dictionary<string, string>());

        rendered.Subject.Length.Should().Be(256);
    }

    [Fact]
    public async Task RenderAsync_uses_override_subject_when_present()
    {
        _repo.GetOverrideAsync(EmailTemplateKey.PasswordReset, Arg.Any<CancellationToken>())
            .Returns(new EmailTemplate
            {
                Key = EmailTemplateKey.PasswordReset,
                SubjectOverride = "Custom subject for {{serverName}}",
                HtmlBodyOverride = null,
                TextBodyOverride = null
            });

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.PasswordReset, new Dictionary<string, string>
        {
            ["serverName"] = "MyServer",
            ["userName"] = "Andy",
            ["resetLink"] = "https://example.com"
        });

        rendered.Subject.Should().Be("Custom subject for MyServer");
        rendered.TextBody.Should().Contain("Andy");
    }

    [Fact]
    public async Task RenderAsync_falls_back_to_builtin_when_override_field_is_blank()
    {
        _repo.GetOverrideAsync(EmailTemplateKey.PasswordReset, Arg.Any<CancellationToken>())
            .Returns(new EmailTemplate
            {
                Key = EmailTemplateKey.PasswordReset,
                SubjectOverride = "   ",
                HtmlBodyOverride = "",
                TextBodyOverride = null
            });

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.PasswordReset, new Dictionary<string, string>
        {
            ["serverName"] = "MyServer",
            ["userName"] = "Andy",
            ["resetLink"] = "https://example.com"
        });

        rendered.Subject.Should().Contain("MyServer");
        rendered.TextBody.Should().Contain("Andy");
        rendered.HtmlBody.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RenderAsync_uses_override_html_body_when_provided()
    {
        _repo.GetOverrideAsync(EmailTemplateKey.RequestAvailable, Arg.Any<CancellationToken>())
            .Returns(new EmailTemplate
            {
                Key = EmailTemplateKey.RequestAvailable,
                HtmlBodyOverride = "<p>Custom HTML for {{userName}}</p>"
            });

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.RequestAvailable, new Dictionary<string, string>
        {
            ["userName"] = "Andy",
            ["mediaTitle"] = "The Matrix",
            ["serverName"] = "Vora",
            ["mediaUrl"] = "https://example.com/m/1"
        });

        rendered.HtmlBody.Should().Be("<p>Custom HTML for Andy</p>");
    }

    [Fact]
    public async Task GetBuiltInAsync_returns_built_in_resources_for_all_keys()
    {
        foreach (var key in Enum.GetValues<EmailTemplateKey>())
        {
            var content = await _renderer.GetBuiltInAsync(key);

            content.Subject.Should().NotBeNullOrWhiteSpace($"key {key} subject");
            content.HtmlBody.Should().NotBeNullOrWhiteSpace($"key {key} html");
            content.TextBody.Should().NotBeNullOrWhiteSpace($"key {key} text");
        }
    }

    [Fact]
    public async Task GetBuiltInAsync_does_not_consult_repository()
    {
        await _renderer.GetBuiltInAsync(EmailTemplateKey.PasswordReset);

        await _repo.DidNotReceiveWithAnyArgs().GetOverrideAsync(default, default);
    }

    [Fact]
    public async Task RenderAsync_ignores_unknown_template_placeholders_silently()
    {
        _repo.GetOverrideAsync(EmailTemplateKey.AdminInvite, Arg.Any<CancellationToken>())
            .Returns(new EmailTemplate
            {
                Key = EmailTemplateKey.AdminInvite,
                SubjectOverride = "Hi {{userName}} from {{nonexistent}}",
                HtmlBodyOverride = "<p>{{userName}} - {{stillMissing}}</p>",
                TextBodyOverride = "Plain {{userName}}"
            });

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.AdminInvite, new Dictionary<string, string>
        {
            ["userName"] = "Andy"
        });

        // Subject sanitization trims trailing whitespace after substitution.
        rendered.Subject.Should().Be("Hi Andy from");
        rendered.HtmlBody.Should().Be("<p>Andy - </p>");
        rendered.TextBody.Should().Be("Plain Andy");
    }

    [Fact]
    public async Task RenderAsync_handles_whitespace_inside_braces()
    {
        _repo.GetOverrideAsync(EmailTemplateKey.AdminInvite, Arg.Any<CancellationToken>())
            .Returns(new EmailTemplate
            {
                Key = EmailTemplateKey.AdminInvite,
                SubjectOverride = "Hello {{ userName }} and {{userName}}",
                HtmlBodyOverride = "ok",
                TextBodyOverride = "ok"
            });

        var rendered = await _renderer.RenderAsync(EmailTemplateKey.AdminInvite, new Dictionary<string, string>
        {
            ["userName"] = "Andy"
        });

        rendered.Subject.Should().Be("Hello Andy and Andy");
    }
}

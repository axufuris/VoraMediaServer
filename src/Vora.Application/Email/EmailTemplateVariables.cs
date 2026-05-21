using Vora.Domain.Enums;

namespace Vora.Application.Email;

public class EmailTemplateVariable
{
    public required string Name { get; init; }
    public required string Description { get; init; }
}

public static class EmailTemplateVariables
{
    public const string UserName = "userName";
    public const string ResetLink = "resetLink";
    public const string InviteLink = "inviteLink";
    public const string InviteEmail = "inviteEmail";
    public const string ServerName = "serverName";
    public const string MediaTitle = "mediaTitle";
    public const string MediaType = "mediaType";
    public const string MediaLink = "mediaLink";
    public const string PosterUrl = "posterUrl";

    private static readonly IReadOnlyDictionary<EmailTemplateKey, IReadOnlyList<EmailTemplateVariable>> Catalog =
        new Dictionary<EmailTemplateKey, IReadOnlyList<EmailTemplateVariable>>
        {
            [EmailTemplateKey.PasswordReset] = new List<EmailTemplateVariable>
            {
                new() { Name = UserName, Description = "The display name of the user requesting the reset." },
                new() { Name = ResetLink, Description = "The single-use password reset link." },
                new() { Name = ServerName, Description = "The configured server name." }
            },
            [EmailTemplateKey.AdminInvite] = new List<EmailTemplateVariable>
            {
                new() { Name = InviteLink, Description = "The single-use registration link." },
                new() { Name = InviteEmail, Description = "The email address the invite was sent to." },
                new() { Name = ServerName, Description = "The configured server name." }
            },
            [EmailTemplateKey.RequestAvailable] = new List<EmailTemplateVariable>
            {
                new() { Name = UserName, Description = "The display name of the requester." },
                new() { Name = MediaTitle, Description = "The title of the requested media item." },
                new() { Name = MediaType, Description = "The media type (Movie, TvShow, etc.)." },
                new() { Name = MediaLink, Description = "Deep link to the media details page." },
                new() { Name = PosterUrl, Description = "URL of the media poster image." },
                new() { Name = ServerName, Description = "The configured server name." }
            },
            [EmailTemplateKey.TestEmail] = new List<EmailTemplateVariable>
            {
                new() { Name = ServerName, Description = "The configured server name." }
            }
        };

    public static IReadOnlyList<EmailTemplateVariable> For(EmailTemplateKey key) =>
        Catalog.TryGetValue(key, out var vars) ? vars : Array.Empty<EmailTemplateVariable>();
}

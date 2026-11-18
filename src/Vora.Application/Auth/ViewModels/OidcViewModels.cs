using Vora.Application.Auth.External;

namespace Vora.Application.Auth.ViewModels;

public class ExternalPendingVM
{
    public ExternalSignInOutcome Outcome { get; set; }
    public ExternalSignInRefusal Refusal { get; set; }
    public string? Message { get; set; }
    public bool CanCreateAccount { get; set; }
    public bool PasswordRejected { get; set; }
    public bool InvitePinRejected { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public class ExternalCompleteRequest
{
    public string Handle { get; set; } = string.Empty;
    public bool CreateAccount { get; set; }
    public string? InvitePin { get; set; }
    public string? LinkEmail { get; set; }
    public string? LinkPassword { get; set; }
}

public class ExternalCompleteVM
{
    public ExternalSignInOutcome Outcome { get; set; }
    public ExternalSignInRefusal Refusal { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }
    public string? ReturnTo { get; set; }
    public bool IsNewAccount { get; set; }
    public bool CanCreateAccount { get; set; }
    public bool PasswordRejected { get; set; }
    public bool InvitePinRejected { get; set; }
}

public class StartLinkRequest
{
    public string? ReturnTo { get; set; }
}

public class StartLinkVM
{
    public string Url { get; set; } = string.Empty;
}

public class OidcTestResultVM
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public string? Issuer { get; set; }
    public string? AuthorizationEndpoint { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? JwksUri { get; set; }
    public string? UserInfoEndpoint { get; set; }
    public int SigningKeyCount { get; set; }
    public bool AdvertisesPkceS256 { get; set; }
    public List<string> Warnings { get; set; } = new();
}

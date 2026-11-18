using Vora.Application.Auth.Dtos;
using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Ldap;
using Vora.Domain.Enums;

namespace Vora.Application.Auth;

public enum PasswordSignInOutcome
{
    SignedIn,
    InvalidCredentials,
    PasswordSignInDisabled,
    NeedsMore,
    Refused
}

public sealed record PasswordSignInResult(
    PasswordSignInOutcome Outcome,
    AuthResponseDto? Auth,
    AuthSignInMethod Method,
    string? PendingHandle,
    ExternalSignInOutcome? PendingKind,
    string? Message)
{
    public static PasswordSignInResult Invalid { get; } = new(PasswordSignInOutcome.InvalidCredentials, null, AuthSignInMethod.Password, null, null, null);
    public static PasswordSignInResult Disabled { get; } = new(PasswordSignInOutcome.PasswordSignInDisabled, null, AuthSignInMethod.Password, null, null, null);
    public static PasswordSignInResult SignedIn(AuthResponseDto auth, AuthSignInMethod method) => new(PasswordSignInOutcome.SignedIn, auth, method, null, null, null);
    public static PasswordSignInResult NeedsMore(string handle, ExternalSignInOutcome kind) => new(PasswordSignInOutcome.NeedsMore, null, AuthSignInMethod.External, handle, kind, null);
    public static PasswordSignInResult Refused(string message) => new(PasswordSignInOutcome.Refused, null, AuthSignInMethod.External, null, null, message);
}

public interface IPasswordSignInService
{
    Task<PasswordSignInResult> SignInAsync(string identifier, string password, CancellationToken cancellationToken);
}

public sealed class PasswordSignInService(IAuthManager auth, ILdapSignInManager directory) : IPasswordSignInService
{
    public async Task<PasswordSignInResult> SignInAsync(string identifier, string password, CancellationToken cancellationToken)
    {
        if (auth.IsSignInLocked(identifier))
        {
            return PasswordSignInResult.Invalid;
        }

        var passwordDisabled = false;
        try
        {
            var local = await auth.LoginAsync(identifier, password);
            if (local is not null)
            {
                return PasswordSignInResult.SignedIn(local, AuthSignInMethod.Password);
            }
        }
        catch (PasswordSignInDisabledException)
        {
            passwordDisabled = true;
        }

        if (await directory.AnyEnabledAsync())
        {
            var result = await directory.SignInAsync(identifier, password, cancellationToken);
            switch (result.Outcome)
            {
                case LdapSignInOutcome.SignedIn when result.UserId is Guid userId:
                    auth.ClearSignInFailures(identifier);
                    var response = await auth.SignInExternalAsync(userId);
                    return response is null ? PasswordSignInResult.Invalid : PasswordSignInResult.SignedIn(response, AuthSignInMethod.External);

                case LdapSignInOutcome.Pending when result.PendingHandle is not null && result.PendingKind is ExternalSignInOutcome kind:
                    auth.ClearSignInFailures(identifier);
                    return PasswordSignInResult.NeedsMore(result.PendingHandle, kind);

                case LdapSignInOutcome.Refused:
                    auth.ClearSignInFailures(identifier);
                    return PasswordSignInResult.Refused(result.Message ?? "This sign-in is not allowed.");
            }
        }

        return passwordDisabled ? PasswordSignInResult.Disabled : PasswordSignInResult.Invalid;
    }
}

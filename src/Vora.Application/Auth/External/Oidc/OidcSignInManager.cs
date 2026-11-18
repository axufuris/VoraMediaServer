using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Auth.ViewModels;
using Vora.Application.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Auth.External.Oidc;

public enum OidcStartOutcome
{
    Redirect,
    Failed
}

public sealed record OidcStartResult(OidcStartOutcome Outcome, string? RedirectUrl, string? ErrorCode, string BaseUrl);

public enum OidcCallbackOutcome
{
    SignedIn,
    Pending,
    Linked,
    Failed
}

public sealed record OidcCallbackResult(OidcCallbackOutcome Outcome, string? Code, string? PendingHandle, string? ErrorCode, string? ReturnTo, bool WasLinkRequest, string Slug, string BaseUrl);

public sealed record OidcStartRequest(string? ReturnTo, string? InviteToken, Guid? LinkAccountId);

public sealed record OidcCallbackParameters(string? Code, string? State, string? Error);

public interface IOidcSignInManager
{
    Task<OidcStartResult> StartAsync(string slug, OidcStartRequest request, string requestOrigin, CancellationToken cancellationToken);
    Task<OidcCallbackResult> CompleteAsync(string slug, OidcCallbackParameters parameters, string requestOrigin, CancellationToken cancellationToken);
    ExternalPendingVM? DescribePending(string handle);
    Task<ExternalCompleteVM> ResumeAsync(ExternalCompleteRequest request, CancellationToken cancellationToken);
    Task<OidcTestResultVM?> TestAsync(Guid providerId, CancellationToken cancellationToken);
}

public sealed class OidcSignInManager(
    IAuthProviderRepository providers,
    ISystemSettingsRepository settingsRepo,
    IOidcProviderClient client,
    IOidcIdTokenValidator validator,
    IOidcLoginStateStore states,
    IExternalPendingStore pendings,
    IExternalSignInCodeStore codes,
    IExternalSignInService signIn,
    ILogger<OidcSignInManager> logger) : IOidcSignInManager
{
    private const int MaxReturnToLength = 512;
    private const int MaxInviteTokenLength = 256;

    public async Task<OidcStartResult> StartAsync(string slug, OidcStartRequest request, string requestOrigin, CancellationToken cancellationToken)
    {
        var baseUrl = await BaseUrlAsync(requestOrigin);
        var provider = await providers.GetBySlugAsync((slug ?? string.Empty).ToLowerInvariant());
        if (!IsUsable(provider))
        {
            return new OidcStartResult(OidcStartOutcome.Failed, null, OidcErrorCodes.ProviderUnavailable, baseUrl);
        }

        OidcDiscovery discovery;
        try
        {
            discovery = await client.GetDiscoveryAsync(provider, refresh: false, cancellationToken);
        }
        catch (OidcProviderException ex)
        {
            logger.LogWarning("The provider {Provider} could not be reached to start a sign-in: {Reason}", provider.Slug, ex.Message);
            return new OidcStartResult(OidcStartOutcome.Failed, null, ex.Code, baseUrl);
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(16));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var redirectUri = AuthRedirectUris.Callback(baseUrl, provider.Slug);

        var state = states.Create(new OidcLoginState(provider.Id, nonce, verifier, redirectUri, SafeReturnTo(request.ReturnTo), CleanInviteToken(request.InviteToken), request.LinkAccountId));

        var url = AppendQuery(discovery.AuthorizationEndpoint,
        [
            ("response_type", "code"),
            ("client_id", provider.ClientId ?? string.Empty),
            ("redirect_uri", redirectUri),
            ("scope", provider.Scopes),
            ("state", state),
            ("nonce", nonce),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256")
        ]);

        return new OidcStartResult(OidcStartOutcome.Redirect, url, null, baseUrl);
    }

    public async Task<OidcCallbackResult> CompleteAsync(string slug, OidcCallbackParameters parameters, string requestOrigin, CancellationToken cancellationToken)
    {
        var baseUrl = await BaseUrlAsync(requestOrigin);
        var state = string.IsNullOrWhiteSpace(parameters.State) ? null : states.Take(parameters.State);
        var wasLink = state?.LinkAccountId is not null;

        OidcCallbackResult Failed(string code) => new(OidcCallbackOutcome.Failed, null, null, code, state?.ReturnTo, wasLink, slug, baseUrl);

        var provider = await providers.GetBySlugAsync((slug ?? string.Empty).ToLowerInvariant());
        if (state is null || provider is null || state.ProviderId != provider.Id)
        {
            return Failed(OidcErrorCodes.StateExpired);
        }

        if (!IsUsable(provider))
        {
            return Failed(OidcErrorCodes.ProviderUnavailable);
        }

        if (!string.IsNullOrWhiteSpace(parameters.Error))
        {
            logger.LogInformation("The provider {Provider} ended a sign-in with the error {Error}", provider.Slug, parameters.Error);
            return Failed(string.Equals(parameters.Error, "access_denied", StringComparison.Ordinal) ? OidcErrorCodes.AccessDenied : OidcErrorCodes.ProviderError);
        }

        if (string.IsNullOrWhiteSpace(parameters.Code))
        {
            return Failed(OidcErrorCodes.ProviderError);
        }

        ExternalClaims claims;
        try
        {
            claims = await ReadClaimsAsync(provider, parameters.Code, state, cancellationToken);
        }
        catch (OidcProviderException ex)
        {
            logger.LogWarning("A sign-in through {Provider} failed ({Code}): {Reason}", provider.Slug, ex.Code, ex.Message);
            return Failed(ex.Code);
        }

        if (state.LinkAccountId is Guid accountId)
        {
            var linked = await signIn.LinkAccountAsync(provider.Id, accountId, claims, cancellationToken);
            return linked.Outcome == ExternalSignInOutcome.SignedIn
                ? new OidcCallbackResult(OidcCallbackOutcome.Linked, null, null, null, state.ReturnTo, true, slug, baseUrl)
                : Failed(linked.Refusal == ExternalSignInRefusal.AlreadyLinked ? OidcErrorCodes.LinkConflict : OidcErrorCodes.LinkRefused);
        }

        var result = await signIn.SignInAsync(provider.Id, claims, new ExternalSignInOptions { InviteToken = state.InviteToken }, cancellationToken);
        if (result.Outcome == ExternalSignInOutcome.SignedIn && result.UserId is Guid userId)
        {
            return new OidcCallbackResult(OidcCallbackOutcome.SignedIn, codes.Issue(userId, provider.Id), null, null, state.ReturnTo, false, slug, baseUrl);
        }

        var handle = pendings.Create(new ExternalPending(provider.Id, provider.DisplayName, claims, state.InviteToken, state.ReturnTo, result, 0));
        return new OidcCallbackResult(OidcCallbackOutcome.Pending, null, handle, null, state.ReturnTo, false, slug, baseUrl);
    }

    public ExternalPendingVM? DescribePending(string handle)
    {
        var pending = pendings.Find(handle);
        return pending is null ? null : Describe(pending);
    }

    public async Task<ExternalCompleteVM> ResumeAsync(ExternalCompleteRequest request, CancellationToken cancellationToken)
    {
        var pending = pendings.Find(request.Handle);
        if (pending is null)
        {
            return Expired();
        }

        var link = !string.IsNullOrWhiteSpace(request.LinkEmail) && !string.IsNullOrEmpty(request.LinkPassword)
            ? new LocalCredentials(request.LinkEmail.Trim(), request.LinkPassword)
            : null;

        var result = await signIn.SignInAsync(
            pending.ProviderId,
            pending.Claims,
            new ExternalSignInOptions { InviteToken = pending.InviteToken, CreateAccount = request.CreateAccount, InvitePin = request.InvitePin, LinkTo = link },
            cancellationToken);

        if (result.Outcome == ExternalSignInOutcome.SignedIn && result.UserId is Guid userId)
        {
            pendings.Remove(request.Handle);
            return new ExternalCompleteVM
            {
                Outcome = ExternalSignInOutcome.SignedIn,
                Code = codes.Issue(userId, pending.ProviderId),
                ReturnTo = pending.ReturnTo,
                IsNewAccount = result.IsNewAccount
            };
        }

        var failures = pending.Failures + (result.PasswordRejected || result.InvitePinRejected ? 1 : 0);
        if (failures >= ExternalPendingStore.MaxFailures)
        {
            pendings.Remove(request.Handle);
            return new ExternalCompleteVM
            {
                Outcome = ExternalSignInOutcome.Refused,
                Message = "Too many wrong attempts. Start the sign-in again."
            };
        }

        pendings.Update(request.Handle, pending with { Last = result, Failures = failures });
        return new ExternalCompleteVM
        {
            Outcome = result.Outcome,
            Refusal = result.Refusal,
            Message = result.Message,
            CanCreateAccount = result.CanCreateAccount,
            PasswordRejected = result.PasswordRejected,
            InvitePinRejected = result.InvitePinRejected
        };
    }

    public async Task<OidcTestResultVM?> TestAsync(Guid providerId, CancellationToken cancellationToken)
    {
        var provider = await providers.GetByIdAsync(providerId);
        if (provider is null)
        {
            return null;
        }

        var result = new OidcTestResultVM();
        if (string.IsNullOrWhiteSpace(provider.Issuer) || string.IsNullOrWhiteSpace(provider.ClientId))
        {
            result.Message = "Enter the issuer and the client id first.";
            return result;
        }

        try
        {
            var discovery = await client.GetDiscoveryAsync(provider, refresh: true, cancellationToken);
            var keys = await client.GetSigningKeysAsync(discovery, refresh: true, cancellationToken);

            result.Ok = true;
            result.Message = "The provider answered and its signing keys were read.";
            result.Issuer = discovery.Issuer;
            result.AuthorizationEndpoint = discovery.AuthorizationEndpoint;
            result.TokenEndpoint = discovery.TokenEndpoint;
            result.JwksUri = discovery.JwksUri;
            result.UserInfoEndpoint = discovery.UserInfoEndpoint;
            result.SigningKeyCount = keys.Count;
            result.AdvertisesPkceS256 = discovery.AdvertisesS256;

            if (!provider.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add("The issuer uses plain http, so tokens and the client secret cross the network unprotected. Use https unless this is a private network you trust.");
            }

            if (!discovery.SupportsS256)
            {
                result.Warnings.Add("The provider says it does not support PKCE with S256, which every sign-in here uses.");
            }
            else if (!discovery.AdvertisesS256)
            {
                result.Warnings.Add("The provider does not list its PKCE methods; sign-ins use S256 and fail if it is not supported.");
            }

            if (string.IsNullOrEmpty(provider.ClientSecretCiphertext))
            {
                result.Warnings.Add("No client secret is saved. That is right only for a public client; most providers need one.");
            }

            if (!provider.Scopes.Split(' ').Contains("email", StringComparer.Ordinal) && discovery.UserInfoEndpoint is null)
            {
                result.Warnings.Add("The email scope is not requested, so no email address will be shared and new accounts cannot be made.");
            }
        }
        catch (OidcProviderException ex)
        {
            result.Message = ex.Message;
        }

        return result;
    }

    private async Task<ExternalClaims> ReadClaimsAsync(AuthProvider provider, string code, OidcLoginState state, CancellationToken cancellationToken)
    {
        var discovery = await client.GetDiscoveryAsync(provider, refresh: false, cancellationToken);
        var tokens = await client.ExchangeCodeAsync(provider, discovery, code, state.RedirectUri, state.CodeVerifier, cancellationToken);
        var idClaims = await validator.ValidateAsync(provider, discovery, tokens.IdToken, state.Nonce, cancellationToken);

        JsonElement? userInfo = null;
        if (tokens.AccessToken is not null && OidcClaimReader.NeedsUserInfo(provider, idClaims))
        {
            userInfo = await client.GetUserInfoAsync(discovery, tokens.AccessToken, cancellationToken);
        }

        return OidcClaimReader.Read(provider, idClaims, userInfo)
            ?? throw new OidcProviderException(OidcErrorCodes.MissingSubject, "The id token has no subject.");
    }

    private ExternalPendingVM Describe(ExternalPending pending) => new()
    {
        Outcome = pending.Last.Outcome,
        Refusal = pending.Last.Refusal,
        Message = pending.Last.Message,
        CanCreateAccount = pending.Last.CanCreateAccount,
        PasswordRejected = pending.Last.PasswordRejected,
        InvitePinRejected = pending.Last.InvitePinRejected,
        ProviderName = pending.ProviderName,
        Email = pending.Claims.Email
    };

    private static ExternalCompleteVM Expired() => new()
    {
        Outcome = ExternalSignInOutcome.Refused,
        Message = "This sign-in has expired. Start it again."
    };

    private async Task<string> BaseUrlAsync(string requestOrigin)
    {
        var settings = await settingsRepo.GetSettingsAsync();
        return AuthRedirectUris.BaseUrl(settings.PublicBaseUrl, settings.EmailPublicBaseUrl, requestOrigin);
    }

    private static bool IsUsable(AuthProvider? provider) =>
        provider is { Enabled: true, Type: AuthProviderType.Oidc } && !string.IsNullOrWhiteSpace(provider.Issuer) && !string.IsNullOrWhiteSpace(provider.ClientId);

    public static string? SafeReturnTo(string? value) =>
        value is { Length: > 0 and <= MaxReturnToLength }
        && value[0] == '/'
        && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.Contains('\\')
        && !value.Any(char.IsControl)
            ? value
            : null;

    private static string? CleanInviteToken(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxInviteTokenLength ? null : trimmed;
    }

    private static string AppendQuery(string endpoint, IEnumerable<(string Key, string Value)> pairs)
    {
        var query = string.Join('&', pairs.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return endpoint + (endpoint.Contains('?') ? "&" : "?") + query;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

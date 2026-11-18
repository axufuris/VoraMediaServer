using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Oidc;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth.Oidc;

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class OidcTestKit
{
    public const string Secret = "the-client-secret";

    public static AuthProvider Provider(Action<AuthProvider>? change = null)
    {
        var provider = new AuthProvider
        {
            Slug = "authentik",
            DisplayName = "Authentik",
            Enabled = true,
            Issuer = MockOidcProvider.Issuer,
            ClientId = MockOidcProvider.ClientId,
            ClientSecretCiphertext = "protected:" + Secret
        };
        change?.Invoke(provider);
        return provider;
    }

    public static IAuthSecretProtector Protector()
    {
        var protector = Substitute.For<IAuthSecretProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(call => call.ArgAt<string>(0)["protected:".Length..]);
        return protector;
    }

    public static OidcProviderClient Client(MockOidcProvider idp, IAuthSecretProtector? protector = null, IMemoryCache? cache = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(OidcProviderClient.HttpClientName).Returns(_ => new HttpClient(idp, disposeHandler: false) { MaxResponseContentBufferSize = 1_048_576 });
        return new OidcProviderClient(factory, cache ?? new MemoryCache(new MemoryCacheOptions()), protector ?? Protector(), NullLogger<OidcProviderClient>.Instance);
    }
}

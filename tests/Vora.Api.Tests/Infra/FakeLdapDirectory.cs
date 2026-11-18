using Vora.Application.Auth.External.Ldap;

namespace Vora.Api.Tests.Infra;

/// <summary>
/// A directory that behaves the way the real one does for the parts Vora relies on:
/// the name is matched against the uid or the mail, a wrong password is invalid
/// credentials, an empty password is never accepted, and when it is down every call is
/// unreachable. It counts the calls so a test can prove the directory was not asked.
/// </summary>
public sealed class FakeLdapDirectory : ILdapDirectory
{
    public sealed record Account(string Uid, string Password, string? Mail, string DisplayName, params string[] Groups)
    {
        public string Dn => $"uid={Uid},ou=people,dc=example,dc=org";
    }

    private int _calls;

    public List<Account> Accounts { get; } = [];

    public bool Up { get; set; } = true;

    public int Calls => Volatile.Read(ref _calls);

    public LdapConnectionSettings? LastSettings { get; private set; }

    public LdapSampleResult Sample { get; set; } = new(true, true, 0, false, 0, 0, 0, [], null);

    public Task<LdapAuthResult> AuthenticateAsync(LdapConnectionSettings settings, string username, string password, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastSettings = settings;

        if (!Up)
        {
            return Task.FromResult(LdapAuthResult.Unreachable("Could not connect to the directory."));
        }

        var matches = Accounts
            .Where(a => string.Equals(a.Uid, username, StringComparison.OrdinalIgnoreCase)
                || (a.Mail is not null && string.Equals(a.Mail, username, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matches.Count != 1 || string.IsNullOrEmpty(password) || matches[0].Password != password)
        {
            return Task.FromResult(LdapAuthResult.InvalidCredentials("The directory did not accept the sign-in."));
        }

        var account = matches[0];
        return Task.FromResult(LdapAuthResult.Authenticated(new LdapUserEntry(account.Dn, "uuid-" + account.Uid, account.Mail, account.DisplayName, account.Groups)));
    }

    public Task<LdapSampleResult> SampleAsync(LdapConnectionSettings settings, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastSettings = settings;
        return Task.FromResult(Sample);
    }
}

using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Novell.Directory.Ldap;
using Vora.Application.Auth.External.Ldap;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Auth;

public sealed class NovellLdapDirectory(ILogger<NovellLdapDirectory> logger) : ILdapDirectory
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(20);
    private const int ConnectTimeoutMilliseconds = 8000;
    private const int SearchTimeLimitMilliseconds = 10000;
    private const int SampleSize = 50;
    private const int SampleDnCount = 3;

    public async Task<LdapAuthResult> AuthenticateAsync(LdapConnectionSettings settings, string username, string password, CancellationToken cancellationToken)
    {
        if (!LdapFilter.IsValidUsername(username) || string.IsNullOrEmpty(password))
        {
            return LdapAuthResult.InvalidCredentials("empty or invalid name or password");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);

        FoundUser found;
        try
        {
            using var service = await OpenAsync(settings, timeout.Token);
            await BindServiceAsync(service, settings, timeout.Token);

            var lookup = await FindUserAsync(service, settings, username, timeout.Token);
            if (lookup.Matches != 1 || lookup.Entry is null)
            {
                return LdapAuthResult.InvalidCredentials(lookup.Matches == 0 ? "no entry matched the name" : "more than one entry matched the name");
            }

            var groups = await FindGroupsAsync(service, settings, lookup.Entry, username, timeout.Token);
            found = new FoundUser(lookup.Entry, groups);
        }
        catch (Exception ex) when (IsDirectoryFailure(ex, cancellationToken))
        {
            return LdapAuthResult.Unreachable(Describe(ex, settings));
        }

        try
        {
            using var user = await OpenAsync(settings, timeout.Token);
            await user.BindAsync(LdapConnection.LdapV3, found.Entry.Dn, password, timeout.Token);
            if (!user.Bound || string.IsNullOrEmpty(user.AuthenticationDn))
            {
                return LdapAuthResult.InvalidCredentials("the directory did not confirm the bind");
            }
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials || ex.ResultCode == LdapException.NoSuchObject || ex.ResultCode == LdapException.InappropriateAuthentication)
        {
            return LdapAuthResult.InvalidCredentials("the directory refused the password");
        }
        catch (Exception ex) when (IsDirectoryFailure(ex, cancellationToken))
        {
            return LdapAuthResult.Unreachable(Describe(ex, settings));
        }

        return LdapAuthResult.Authenticated(ToUser(found.Entry, settings, found.Groups));
    }

    public async Task<LdapSampleResult> SampleAsync(LdapConnectionSettings settings, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);

        var connected = false;
        var bound = false;
        try
        {
            using var service = await OpenAsync(settings, timeout.Token);
            connected = true;
            await BindServiceAsync(service, settings, timeout.Token);
            bound = true;

            var entries = new List<LdapEntry>();
            var limited = false;
            var constraints = new LdapSearchConstraints { MaxResults = SampleSize, ServerTimeLimit = SearchTimeLimitMilliseconds / 1000, TimeLimit = SearchTimeLimitMilliseconds };
            var results = await service.SearchAsync(settings.UserBaseDn, LdapConnection.ScopeSub, LdapFilter.BuildProbeFilter(settings.UserFilter), Attributes(settings), false, constraints, timeout.Token);

            try
            {
                while (await results.HasMoreAsync(timeout.Token))
                {
                    try
                    {
                        entries.Add(await results.NextAsync(timeout.Token));
                    }
                    catch (LdapReferralException)
                    {
                        continue;
                    }
                }
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.SizeLimitExceeded)
            {
                limited = true;
            }

            limited |= entries.Count >= SampleSize;

            var withMail = entries.Count(e => !string.IsNullOrWhiteSpace(ReadString(e, settings.MailAttribute)));
            var withUniqueId = entries.Count(e => !string.IsNullOrWhiteSpace(ReadUniqueId(e, settings.UniqueIdAttribute)));

            return new LdapSampleResult(true, true, entries.Count, limited, withMail, entries.Count - withMail, withUniqueId, entries.Take(SampleDnCount).Select(e => e.Dn).ToList(), null);
        }
        catch (Exception ex) when (IsDirectoryFailure(ex, cancellationToken))
        {
            return new LdapSampleResult(connected, bound, 0, false, 0, 0, 0, [], Describe(ex, settings));
        }
    }

    private static async Task<LdapConnection> OpenAsync(LdapConnectionSettings settings, CancellationToken cancellationToken)
    {
        var options = new LdapConnectionOptions();
        if (settings.Security == LdapSecurityMode.Ldaps)
        {
            options.UseSsl();
        }

        if (settings.Security != LdapSecurityMode.None)
        {
            options.ConfigureRemoteCertificateValidationCallback((_, certificate, chain, errors) =>
                LdapCertificateValidator.IsTrusted(certificate, chain, errors, settings.CaCertificatePem));
        }

        var connection = new LdapConnection(options) { ConnectionTimeout = ConnectTimeoutMilliseconds };
        try
        {
            await connection.ConnectAsync(settings.Host, settings.Port, cancellationToken);
            if (settings.Security == LdapSecurityMode.StartTls)
            {
                await connection.StartTlsAsync(cancellationToken);
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static async Task BindServiceAsync(LdapConnection connection, LdapConnectionSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.BindDn))
        {
            return;
        }

        await connection.BindAsync(LdapConnection.LdapV3, settings.BindDn, settings.BindPassword ?? string.Empty, cancellationToken);
    }

    private static async Task<UserLookup> FindUserAsync(LdapConnection connection, LdapConnectionSettings settings, string username, CancellationToken cancellationToken)
    {
        var constraints = new LdapSearchConstraints { MaxResults = 2, ServerTimeLimit = SearchTimeLimitMilliseconds / 1000, TimeLimit = SearchTimeLimitMilliseconds };
        var results = await connection.SearchAsync(settings.UserBaseDn, LdapConnection.ScopeSub, LdapFilter.BuildUserFilter(settings.UserFilter, username), Attributes(settings), false, constraints, cancellationToken);

        var entries = new List<LdapEntry>();
        try
        {
            while (await results.HasMoreAsync(cancellationToken))
            {
                try
                {
                    entries.Add(await results.NextAsync(cancellationToken));
                }
                catch (LdapReferralException)
                {
                    continue;
                }

                if (entries.Count > 1)
                {
                    break;
                }
            }
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.SizeLimitExceeded)
        {
            return new UserLookup(null, 2);
        }

        return new UserLookup(entries.Count == 1 ? entries[0] : null, entries.Count);
    }

    private async Task<IReadOnlyList<string>> FindGroupsAsync(LdapConnection connection, LdapConnectionSettings settings, LdapEntry user, string username, CancellationToken cancellationToken)
    {
        var groups = new List<string>(ReadStrings(user, settings.GroupAttribute));
        if (string.IsNullOrEmpty(settings.GroupBaseDn) || string.IsNullOrEmpty(settings.GroupFilter))
        {
            return groups;
        }

        var constraints = new LdapSearchConstraints { MaxResults = 500, ServerTimeLimit = SearchTimeLimitMilliseconds / 1000, TimeLimit = SearchTimeLimitMilliseconds };
        var results = await connection.SearchAsync(settings.GroupBaseDn, LdapConnection.ScopeSub, LdapFilter.BuildGroupFilter(settings.GroupFilter, user.Dn, username), ["cn"], false, constraints, cancellationToken);

        try
        {
            while (await results.HasMoreAsync(cancellationToken))
            {
                LdapEntry group;
                try
                {
                    group = await results.NextAsync(cancellationToken);
                }
                catch (LdapReferralException)
                {
                    continue;
                }

                groups.Add(group.Dn);
                if (ReadString(group, "cn") is { Length: > 0 } name)
                {
                    groups.Add(name);
                }
            }
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.SizeLimitExceeded)
        {
            logger.LogWarning("A group search returned more than the 500 groups Vora reads for one person; the rest are ignored");
        }

        return groups;
    }

    private static LdapUserEntry ToUser(LdapEntry entry, LdapConnectionSettings settings, IReadOnlyList<string> groups) =>
        new(entry.Dn, ReadUniqueId(entry, settings.UniqueIdAttribute), ReadString(entry, settings.MailAttribute), ReadString(entry, settings.DisplayNameAttribute), groups);

    private static string[] Attributes(LdapConnectionSettings settings) =>
        new[] { settings.MailAttribute, settings.DisplayNameAttribute, settings.UniqueIdAttribute, settings.GroupAttribute }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static LdapAttribute? Find(LdapEntry entry, string attribute) =>
        entry.Contains(attribute) ? entry.Get(attribute) : null;

    private static string? ReadString(LdapEntry entry, string attribute)
    {
        var value = Find(entry, attribute)?.StringValue;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IEnumerable<string> ReadStrings(LdapEntry entry, string attribute) =>
        Find(entry, attribute)?.StringValueArray?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? [];

    private static string? ReadUniqueId(LdapEntry entry, string attribute)
    {
        var found = Find(entry, attribute);
        if (found is null)
        {
            return null;
        }

        var bytes = found.ByteValue;
        if (bytes is { Length: 16 } && attribute.Contains("guid", StringComparison.OrdinalIgnoreCase))
        {
            return new Guid(bytes).ToString();
        }

        var text = found.StringValue;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool IsDirectoryFailure(Exception ex, CancellationToken callerToken) =>
        ex is LdapException or SocketException or IOException or TimeoutException or InvalidOperationException
            or System.Security.Authentication.AuthenticationException
        || (ex is OperationCanceledException && !callerToken.IsCancellationRequested);

    private static string Describe(Exception ex, LdapConnectionSettings settings)
    {
        switch (ex)
        {
            case LdapException { ResultCode: LdapException.InvalidCredentials }:
                return "The bind account or its password was refused by the directory.";
            case LdapException { ResultCode: LdapException.NoSuchObject }:
                return "The search base or the bind account does not exist in the directory.";
            case LdapException { ResultCode: LdapException.StrongAuthRequired or LdapException.ConfidentialityRequired }:
                return "The directory requires an encrypted connection; use ldaps or StartTLS.";
            case LdapException { ResultCode: LdapException.InsufficientAccessRights }:
                return "The bind account is not allowed to search the directory.";
            case LdapException { ResultCode: LdapException.FilterError }:
                return "The directory rejected the search filter.";
            case LdapException { ResultCode: LdapException.SslHandshakeFailed or LdapException.TlsNotSupported }:
                return settings.Security == LdapSecurityMode.StartTls
                    ? "The directory did not accept StartTLS, or its certificate was not trusted."
                    : "The secure connection failed; check the port, and the certificate authority if the directory uses a private one.";
            case LdapException { ResultCode: LdapException.ServerDown or LdapException.ConnectError or LdapException.Unavailable or LdapException.Busy }:
                return $"Could not connect to {settings.Host}:{settings.Port}.";
            case LdapException e:
                return $"The directory answered with an error ({e.ResultCodeToString()}).";
            case System.Security.Authentication.AuthenticationException:
                return "The directory's certificate was not trusted or does not match the host name; add its certificate authority.";
            case SocketException:
                return $"Could not connect to {settings.Host}:{settings.Port}.";
            case OperationCanceledException:
                return "The directory did not answer in time.";
            default:
                return "The directory could not be used.";
        }
    }

    private sealed record FoundUser(LdapEntry Entry, IReadOnlyList<string> Groups);

    private sealed record UserLookup(LdapEntry? Entry, int Matches);
}

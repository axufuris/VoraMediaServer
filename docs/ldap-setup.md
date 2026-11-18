# Setting up an LDAP directory

Field values for **Users & Access → Sign-in providers → Sign in with a directory (LDAP)**. How it works, and what the login answers, is in [`docs/sso.md`](sso.md#ldap). Every address below is an example: use your own. Everyone who signs in needs an email address in the directory (the **mail** attribute), because Vora makes an account from it; the **Test connection** button counts the people who have none. Every value below was checked against the product's own documentation or source (links at the end), but none was verified against a live server.

## Before any recipe

1. **A read-only service account** in the directory, never an administrator. Vora uses it to find people, then checks each password by signing in as the person.
2. **Encryption.** Pick LDAPS (usually port 636) or StartTLS (389). If the directory's certificate comes from your own authority, paste that authority's PEM certificate into *Certificate authority*. The certificate must also be made for the host you enter, so use the name on it, not an IP address. Plain LDAP is allowed only to a host on a private network, after a tick box.
3. **Press Test connection** after saving. It reaches the directory, signs in as the service account, searches, and shows how many people it found, how many have a mail, and three example entries.
4. **Turn it on last.** Then the login page says **Email or username**.

The user filter finds one person: `{username}` becomes what was typed (escaped). Matching `mail` as well as the username lets people sign in with their email too. If a filter matches two entries, nobody signs in.

| Test says | Usually means |
| --- | --- |
| The bind account or its password was refused | Wrong bind DN or password. Re-type the password: a saved one is never shown. |
| The search base or the bind account does not exist | A typo in a DN. Copy it from the directory. |
| The bind account is not allowed to search | An access rule: give the account read on the people and their attributes. |
| The directory's certificate was not trusted or does not match the host name | Paste the authority's certificate, and enter the host name the certificate names. |
| Could not connect | Wrong host or port, a firewall, or the wrong encryption for the port. |
| None of the entries checked has the mail attribute | The mail attribute name is wrong for this directory. |

## OpenLDAP

| Field | Value |
| --- | --- |
| Host, port, encryption | `ldap.example.com`, 636, LDAPS (or 389, StartTLS) |
| Bind DN | `cn=vora,ou=service,dc=example,dc=com` |
| User search base | `ou=people,dc=example,dc=com` |
| Mail, display name | `mail`, `displayName` |
| Unique id | `entryUUID` |
| Group attribute | `memberOf` (needs the memberOf overlay) |

User filter:

```
(&(objectClass=inetOrgPerson)(|(uid={username})(mail={username})))
```

Without the overlay, fill in the group search: base `ou=groups,dc=example,dc=com` and filter `(&(objectClass=groupOfNames)(member={dn}))`, or for POSIX groups `(&(objectClass=posixGroup)(memberUid={username}))`. `{username}` is what was typed, so the POSIX filter finds a person's groups only when they type their login name, not their email. The service account needs `read` on `entry`, `uid`, `mail`, `displayName`, `entryUUID` and `memberOf` under the people base in the access rules (`olcAccess`), and never `userPassword`.

## LLDAP

| Field | Value |
| --- | --- |
| Host, port, encryption | `lldap.example.com`, 6360, LDAPS (turn on `ldaps_options` in LLDAP's config, with a certificate and key). LLDAP's documentation offers LDAPS only (STARTTLS appears there only for its email settings); its plain port, 3890, is for a trusted network only |
| Bind DN | `uid=vora,ou=people,dc=example,dc=com`, a user in the `lldap_strict_readonly` group |
| User search base | `ou=people,dc=example,dc=com` |
| Mail, display name | `mail`, `displayName` |
| Unique id | `uuid` |
| Group attribute | `memberOf` (a group name such as `staff` works as the required group) |

User filter:

```
(&(objectClass=person)(|(uid={username})(mail={username})))
```

LLDAP's README does not list its LDAP attributes: `uuid`, `displayName` (LLDAP also answers to `cn` for it) and the `person` object class are read from LLDAP's source code, not verified against a live server.

## Authentik LDAP outpost

In Authentik create an LDAP provider, an application and an LDAP outpost. Make a service user (here `vora-service`) with a password, put it in a role, and give that role the **Search full LDAP directory** permission on the provider (its Permissions tab). Without that permission a user can search only their own entry, so the user search finds nobody else. The service user and every person who signs in also need access to the application: Authentik refuses a bind and search to anyone without it.

| Field | Value |
| --- | --- |
| Host, port, encryption | `ldap.example.com` (the outpost), 636, LDAPS (or 389, StartTLS). Set the provider's *Certificate* and *TLS Server Name* (the host name you enter here), and paste the certificate's authority if it is private. The outpost's Docker example publishes 389 and 636 from 3389 and 6636 inside the container |
| Bind DN | `cn=vora-service,ou=users,dc=ldap,dc=goauthentik,dc=io` (`cn=` is the service user's username; the part after `ou=users,` is the provider's Base DN, which defaults to `dc=ldap,dc=goauthentik,dc=io` and must differ between providers) |
| User search base | `ou=users,dc=ldap,dc=goauthentik,dc=io` (groups are under `ou=groups,` the same Base DN) |
| Mail, display name | `mail`, `name` (`displayName` also works) |
| Unique id | `uid` (Authentik's unique user identifier; it does not depend on the user name) |
| Group attribute | `memberOf` (values like `cn=staff,ou=groups,dc=ldap,dc=goauthentik,dc=io`) |

User filter (the username is `cn` here: Authentik's `uid` is its unique id, not the user name; `user` is the object class its own `ldapsearch` example searches for):

```
(&(objectClass=user)(|(cn={username})(mail={username})))
```

- **A second factor.** If the provider's bind flow has an authenticator validation stage, Authentik wants the code after the password, separated by a semicolon (`example-password;123456`). What is typed in Vora's password box goes to the bind as it is, so people type it that way.
- **Search mode.** In Direct search mode the outpost narrows the search in Authentik only for plain equality tests such as `cn=` or `mail=`, so the either-or filter above makes it list every user and filter them itself. That is fine for a home server. For thousands of people use Cached search mode (the outpost keeps the list in memory: faster, but it can be out of date), or the filter `(&(objectClass=user)(cn={username}))` with usernames only. Authentik's page describes the two modes; the filter detail is from the outpost's source code.
- Authentik's page names LDAPS on 636 and plain LDAP on 389 and says StartTLS is supported without naming its port, so 389 for StartTLS is not confirmed. The page lists `cn`, `uid`, `mail`, `name`, `displayName` and `memberOf`; the outpost's source also returns `sAMAccountName` and the `inetOrgPerson` object class, which the page does not list.

## Active Directory

| Field | Value |
| --- | --- |
| Host, port, encryption | `dc1.corp.example.com`, 636, LDAPS. The domain controller needs a certificate for that name (from your own authority: paste it) |
| Bind DN | `svc-vora@corp.example.com` (the UPN form works) or `CN=svc-vora,OU=Service Accounts,DC=corp,DC=example,DC=com` |
| User search base | `DC=corp,DC=example,DC=com`, or a narrower `OU=Staff,DC=corp,DC=example,DC=com` |
| Mail, display name | `mail`, `displayName` |
| Unique id | `objectGUID` (a GUID, kept when the account is renamed) |
| Group attribute | `memberOf` (direct groups only, not nested ones: use the group search below for those) |

User filter (leaves out disabled accounts):

```
(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(|(sAMAccountName={username})(userPrincipalName={username})(mail={username})))
```

A locked-out or expired account fails the sign-in at the directory like a wrong password (Microsoft's pages do not spell out the error it returns: not verified against a live server). For **nested groups** fill in the group search with base `DC=corp,DC=example,DC=com` and filter `(&(objectClass=group)(member:1.2.840.113556.1.4.1941:={dn}))`, then give the required group as its name. A user with no value in `mail` cannot sign in: fill it in AD.

## FreeIPA

| Field | Value |
| --- | --- |
| Host, port, encryption | `ipa.example.com`, 636, LDAPS (or 389, StartTLS). Paste the contents of `/etc/ipa/ca.crt`, which is on every enrolled host, as the authority |
| Bind DN | A system account: `uid=vora,cn=sysaccounts,cn=etc,dc=example,dc=com`. Create it as the Directory Manager with `ldapmodify` and `changetype: add`, with the object classes `account` and `simplesecurityobject` and a `userPassword` in the entry (FreeIPA's LDAP HowTo shows the entry), or with `ipa sysaccount-add vora --random` where your FreeIPA has that command |
| User search base | `cn=users,cn=accounts,dc=example,dc=com` |
| Mail, display name | `mail`, `displayName` |
| Unique id | `ipaUniqueID` |
| Group attribute | `memberOf` (values like `cn=staff,cn=groups,cn=accounts,dc=example,dc=com`) |

User filter:

```
(&(objectClass=person)(|(uid={username})(mail={username})))
```

FreeIPA's documentation does not list the user attributes: `ipaUniqueID`, `displayName`, `mail`, `memberOf` and the `person` object class are read from its source code (the user plugin and `bootstrap-template.ldif`), not verified against a live server.

## Limits and groups

**Limit sign-in to a group** with *Required group*: the group's name (`staff`) or its full DN, matched against `memberOf` and the group search. **Administrators** come only from the opt-in administrator group, never from the directory by default. *Allowed email domains* narrows by the mail's domain. The new-person and link-by-email choices are the same as for OpenID Connect (`docs/sso.md`); the directory's mail counts as verified, so use *Link automatically* only for a directory you run.

## Where the values come from

- Authentik: [LDAP provider](https://docs.goauthentik.io/add-secure-apps/providers/ldap/), [creating an LDAP provider](https://docs.goauthentik.io/add-secure-apps/providers/ldap/generic_setup), [the Docker outpost](https://docs.goauthentik.io/add-secure-apps/outposts/manual-deploy-docker-compose/), and the outpost's source, `internal/outpost/ldap` in [goauthentik/authentik](https://github.com/goauthentik/authentik).
- LLDAP: the [README](https://github.com/lldap/lldap/blob/main/README.md) (General configuration guide), [lldap_config.docker_template.toml](https://github.com/lldap/lldap/blob/main/lldap_config.docker_template.toml), [example_configs/authelia.md](https://github.com/lldap/lldap/blob/main/example_configs/authelia.md), and `crates/ldap` in [lldap/lldap](https://github.com/lldap/lldap).
- OpenLDAP: the admin guide's [Access control](https://www.openldap.org/doc/admin26/access-control.html) and [Overlays](https://www.openldap.org/doc/admin26/overlays.html) (memberof), [RFC 4530](https://www.rfc-editor.org/rfc/rfc4530.html) (entryUUID), [RFC 2798](https://www.rfc-editor.org/rfc/rfc2798.html) (inetOrgPerson), [RFC 2307](https://www.rfc-editor.org/rfc/rfc2307.html) (posixGroup).
- Active Directory, on Microsoft Learn: [Search filter syntax](https://learn.microsoft.com/en-us/windows/win32/adsi/search-filter-syntax), [LDAP over SSL](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/enable-ldap-over-ssl-3rd-certification-authority), [UserAccountControl flags](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/useraccountcontrol-manipulate-account-properties), [User naming attributes](https://learn.microsoft.com/en-us/windows/win32/ad/naming-properties), [Group objects](https://learn.microsoft.com/en-us/windows/win32/ad/group-objects), [MS-ADTS simple authentication](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/6a5891b8-928e-4b75-a4a5-0e3b77eaca52).
- FreeIPA: [HowTo/LDAP](https://www.freeipa.org/page/HowTo/LDAP), [LDAP system accounts](https://freeipa.readthedocs.io/en/latest/designs/sysaccounts.html), and the source in [freeipa/freeipa](https://github.com/freeipa/freeipa).

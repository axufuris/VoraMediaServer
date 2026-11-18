# Setting up an LDAP directory

Field values for **Users & Access → Sign-in providers → Sign in with a directory (LDAP)**. How it works, and what the login answers, is in [`docs/sso.md`](sso.md#ldap). Every address below is an example: use your own. Everyone who signs in needs an email address in the directory (the **mail** attribute), because Vora makes an account from it; the **Test connection** button counts the people who have none.

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

Without the overlay, fill in the group search: base `ou=groups,dc=example,dc=com` and filter `(&(objectClass=groupOfNames)(member={dn}))`, or for POSIX groups `(&(objectClass=posixGroup)(memberUid={username}))`. The service account needs `read` on `entry`, `uid`, `mail`, `displayName` and `entryUUID` under the people base in the access rules (`olcAccess`), and never `userPassword`.

## LLDAP

| Field | Value |
| --- | --- |
| Host, port, encryption | `lldap.example.com`, 6360, LDAPS (turn on `ldaps_options` in LLDAP's config). LLDAP has no StartTLS; its plain port, 3890, is for a trusted network only |
| Bind DN | `uid=vora,ou=people,dc=example,dc=com`, a user in the `lldap_strict_readonly` group |
| User search base | `ou=people,dc=example,dc=com` |
| Mail, display name | `mail`, `displayName` |
| Unique id | `uuid` |
| Group attribute | `memberOf` (a group name such as `staff` works as the required group) |

User filter:

```
(&(objectClass=person)(|(uid={username})(mail={username})))
```

## Authentik LDAP outpost

Create an LDAP provider, an application and an LDAP outpost in Authentik, and a service user that is allowed to search the directory in the provider's permissions.

| Field | Value |
| --- | --- |
| Host, port, encryption | `ldap.example.com` (the outpost), 636, LDAPS (or 389, StartTLS). Give the outpost a certificate, and paste its authority if it is private |
| Bind DN | `cn=vora-service,ou=users,dc=ldap,dc=goauthentik,dc=io` (the part after `ou=users,` is the provider's base DN) |
| User search base | `ou=users,dc=ldap,dc=goauthentik,dc=io` |
| Mail, display name | `mail`, `name` |
| Unique id | `uid` (Authentik's stable hash of the user) |
| Group attribute | `memberOf` |

User filter (the username is `cn` here, not `uid`):

```
(&(objectClass=user)(|(cn={username})(mail={username})))
```

## Active Directory

| Field | Value |
| --- | --- |
| Host, port, encryption | `dc1.corp.example.com`, 636, LDAPS. The domain controller needs a certificate for that name (from your own authority: paste it) |
| Bind DN | `svc-vora@corp.example.com` (the UPN form works) or `CN=svc-vora,OU=Service Accounts,DC=corp,DC=example,DC=com` |
| User search base | `DC=corp,DC=example,DC=com`, or a narrower `OU=Staff,DC=corp,DC=example,DC=com` |
| Mail, display name | `mail`, `displayName` |
| Unique id | `objectGUID` (a GUID, kept when the account is renamed) |
| Group attribute | `memberOf` (the person's own groups only) |

User filter (leaves out disabled accounts):

```
(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(|(sAMAccountName={username})(userPrincipalName={username})(mail={username})))
```

A locked-out or expired account fails the sign-in at the directory like a wrong password. For **nested groups** fill in the group search with base `DC=corp,DC=example,DC=com` and filter `(&(objectClass=group)(member:1.2.840.113556.1.4.1941:={dn}))`, then give the required group as its name. A user with no value in `mail` cannot sign in: fill it in AD.

## FreeIPA

| Field | Value |
| --- | --- |
| Host, port, encryption | `ipa.example.com`, 636, LDAPS. Paste the contents of `/etc/ipa/ca.crt` as the authority |
| Bind DN | A system account: `uid=vora,cn=sysaccounts,cn=etc,dc=example,dc=com` (add it with `ldapadd`, then set its password) |
| User search base | `cn=users,cn=accounts,dc=example,dc=com` |
| Mail, display name | `mail`, `displayName` |
| Unique id | `ipaUniqueID` |
| Group attribute | `memberOf` (values like `cn=staff,cn=groups,cn=accounts,dc=example,dc=com`) |

User filter:

```
(&(objectClass=person)(|(uid={username})(mail={username})))
```

## Limits and groups

**Limit sign-in to a group** with *Required group*: the group's name (`staff`) or its full DN, matched against `memberOf` and the group search. **Administrators** come only from the opt-in administrator group, never from the directory by default. *Allowed email domains* narrows by the mail's domain. The new-person and link-by-email choices are the same as for OpenID Connect (`docs/sso.md`); the directory's mail counts as verified, so use *Link automatically* only for a directory you run.

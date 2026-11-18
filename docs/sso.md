# Sign-in providers (SSO)

Vora is self-hosted and has no domain of its own, so every server is its own client of whatever identity provider its admin chooses (Authentik, Keycloak, Authelia, Pocket ID, Google, Microsoft Entra, any OpenID Connect server). The admin registers Vora at the provider with a redirect address of their own server, then pastes the issuer, client id and secret into **Users & Access → Sign-in providers**. Vora ships no client id and talks to no central service.

This page is the design and the server rules. The sign-in flow itself (redirects, the callback, the web and TV screens) arrives in later PRs; what is in the server today is the data, the rules, the one-time code and the admin API, and none of it has a screen yet. Nothing changes for a server whose admin adds no provider. How tokens work is in [`docs/auth-tokens.md`](auth-tokens.md); registration modes and invitations are in [`docs/auth-and-devices.md`](auth-and-devices.md).

## What is stored

| Table | Holds |
| --- | --- |
| `AuthProviders` | One row per provider: `Type` (`Oidc`; `Ldap` is reserved and refused for now), `Slug` (the address name in the redirect URI, unique), `DisplayName` (the button), `Enabled`, `IconUrl`, `SortOrder`, `Issuer`, `ClientId`, `ClientSecretCiphertext`, `Scopes` (must include `openid`), the claim names read for email, name and groups, the two policies below, `AllowedEmailDomains`, `RequiredGroup`, `AdminGroup`. |
| `UserExternalIdentities` | A provider account linked to a user: `UserId`, `ProviderId`, `Subject` (the provider's permanent user id), the email seen when it was linked, `LinkedAt`, `LastSignInAt`. Unique on `(ProviderId, Subject)`; deleted with the user or the provider. |
| `Users.HasPassword` | `false` for an account that signs in only through a provider (it holds an unusable random hash). Setting a password, in account settings or through a reset, makes it `true`. |
| `AuthRefreshTokens.SignInMethod` | `Password` or `External`: how the session began. |
| `ServerSettings.PasswordSignInEnabled`, `PublicBaseUrl` | The password switch (on by default) and the server's public address. |

The client secret is stored as DataProtection ciphertext (`IAuthSecretProtector`, purpose `Vora.Auth.ProviderSecret.v1`), like the SMTP password, so it needs the same keys (`docs/email.md`). It is write-only: no endpoint returns it, the admin VM carries `hasSecret`, and an update that leaves `clientSecret` out keeps it (`clearClientSecret` removes it).

## Who may sign in, and what happens the first time

`ExternalSignInService.SignInAsync(providerId, claims, options)` takes the claims a provider vouched for (subject, email, whether the email is verified, name, groups) and answers `SignedIn`, `NeedsLinkConfirmation`, `NeedsInvitePin`, `NeedsCreateOrLink` or `Refused` with a reason. The decisions are a pure function, `ExternalSignInRules`, tested cell by cell. In order:

1. A provider that is turned off refuses everyone.
2. **`RequiredGroup`** (when set) and **`AllowedEmailDomains`** (when any) gate every sign-in, linked accounts included.
3. A **linked identity** (`provider + subject`) signs in. Sign-in matches the subject, never the email, so an email changed at the provider cannot move or hijack an account.
4. A person who proves a **local password** (`options.LinkTo`) links that account, whatever the policies say: the password is the proof.
5. An **email match** with an existing account follows the provider's `LinkByEmail`:
   - `Never`: refused, "sign in the way you did before and link this from Account settings".
   - Never on an **unverified email**, whatever the policy.
   - The account has a password: `Automatic` links; `ConfirmWithPassword` (the default) answers `NeedsLinkConfirmation` and links once the right password is given.
   - The account has **no password** and no linked provider (one an admin added without a password): it links on the first verified sign-in. One that already signs in through another provider links only under `Automatic`.
6. **No account**: the person is asked to create one or link an existing one (`NeedsCreateOrLink`, with `canCreateAccount` saying whether creating is allowed). Creating follows `NewUserPolicy`:
   - `ExistingOnly`: never.
   - `IdpDecides`: always (the provider is the gatekeeper, optionally narrowed by group or domain). An explicit choice, never the default.
   - `FollowRegistrationMode` (the **default**): `Disabled` refuses; `Simple` (Open) creates; `SecretWord` asks for the 4-digit invite PIN (`NeedsInvitePin`, used up only when the account is made); `Invitation` needs the per-email invitation token, valid, for the **verified** email the provider gave, and uses it up. A person holding such an invitation is not asked to choose between creating and linking.
   - A new account needs an email. A provider that sends none can still link an existing account by password.

An account made here is built by `IAuthManager.CreateExternalUserAsync`, the same code as registration, so it gets the same first profile and defaults, with no usable password. Nobody becomes an administrator through a provider unless the admin set **`AdminGroup`** and the person is in it: that promotes on sign-in (and rotates the security stamp, ending older tokens), and never demotes. First-run setup stays password-only and a provider can never claim the server.

## The password switch

`ServerSettings.PasswordSignInEnabled` is on by default and an admin must turn it off on purpose (`PUT /api/admin/auth-settings`, refused while no provider is turned on). Off, **everyone except administrators** is refused a password sign-in (403 titled "Password sign-in is turned off", not 401, which clients read as an expired session). Admin accounts keep their passwords, so an admin can never lock themselves out and there is no recovery switch to build. Also off: registering with a password, and an admin adding an account with one (add it without a password). A refresh token from a **password** session stops working for a non-admin (the family is revoked); provider sessions keep refreshing. `POST /auth/login` verification and linking use the same lockout and rate limits (`docs/auth-tokens.md`).

## Linked sign-ins

`GET /api/auth/identities` lists the caller's linked providers and `DELETE /api/auth/identities/{id}` unlinks one, refused with 409 when it is the **last way in**: no password that works (`HasPassword`, and password sign-in on or the person an admin) and no other linked provider that is turned on. Removing a provider (`DELETE /api/admin/auth-providers/{id}`) is refused with 409 when accounts could then no longer sign in, unless `?force=true`.

## The one-time code

A finished provider sign-in hands the browser or app a code, never a token (a token in a URL ends up in history and logs). `IExternalSignInCodeStore` keeps 32 random bytes per code, hashed, in memory, for 60 seconds, single use. `POST /api/auth/external/exchange { code }` (anonymous, `AuthStrict`) trades it for the same `accessToken` a login gives plus a `refreshToken` recorded as an `External` session; a used, expired or unknown code is 401.

## Public address and the redirect URI

The address a provider must be told is `{base}/api/auth/oidc/{slug}/callback`. `{base}` is `ServerSettings.PublicBaseUrl`, else the email link address (`EmailPublicBaseUrl`), else the address the admin's browser used. The admin VM returns the exact `redirectUri` for the admin page to show with a copy button. It must match what is registered at the provider character for character, so set the public address when Vora is behind a reverse proxy.

## Admin API

Admin-only, under `/api/admin/auth-providers` (list, get, create, update, delete) and `/api/admin/auth-settings` (get, update `{ passwordSignInEnabled, publicBaseUrl }`). Anonymous `GET /api/auth/providers` lists what the login page needs: `{ providers: [{ id, slug, displayName, type, iconUrl }], passwordSignInEnabled, registrationMode }`, turned-on providers only, nothing about their configuration. Admin **Add user** takes `ssoOnly: true` to add an account without a password, which links on the person's first verified provider sign-in.

## Backups

`settings.auth-providers` (the providers; secrets decrypt only with the matching DataProtection keys) and `users.external-identities` (the links; skipped for a user or provider the server lacks). See `docs/backups.md`.

## Not built yet

The OIDC redirect and callback (PR 2), the admin page and login buttons (web), the TV pairing code flow, the Android phone return, and LDAP. See issue #294.

# Sign-in providers (SSO)

Vora is self-hosted and has no domain of its own, so every server is its own client of whatever identity provider its admin chooses (Authentik, Keycloak, Authelia, Pocket ID, Google, Microsoft Entra, any OpenID Connect server). The admin registers Vora at the provider with a redirect address of their own server, then pastes the issuer, client id and secret into **Users & Access → Sign-in providers**. Vora ships no client id and talks to no central service.

This page is the design and the server rules. The server side of the OpenID Connect sign-in is complete (data, rules, the redirect flow, the one-time code, the admin API); the screens for it (web login buttons and admin page, TV pairing, the Android phone return) arrive in later PRs, so today it is driven only through the API. Nothing changes for a server whose admin adds no provider. How tokens work is in [`docs/auth-tokens.md`](auth-tokens.md); registration modes and invitations are in [`docs/auth-and-devices.md`](auth-and-devices.md).

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

## The redirect flow

Authorization code flow with PKCE (S256), always. The browser, not the app, talks to the provider, so it works with anything that speaks OpenID Connect.

1. `GET /api/auth/oidc/{slug}/start?returnTo=&invite=` (anonymous, `AuthBurst`). Reads the provider's discovery document (cached an hour), keeps a server-side record for ten minutes (a random `state`, a `nonce`, the PKCE verifier, the exact redirect URI, `returnTo`, the invite token), and answers 302 to the authorization endpoint. `returnTo` is kept only when it is a path on this server (starts with one `/`, no `//`, `\`, scheme or control character, 512 characters at most).
2. `GET /api/auth/oidc/{slug}/callback` takes `code`, `state`, `error`. The state works **once** and only for the provider it was made for. The server trades the code on the back channel (secret in the Basic header, or in the body for a provider that lists only `client_secret_post`; none for a public client), then validates the id token and reads the claims. Then it answers 302 to the web app (the public address):
   - `/login/external?code=…&returnTo=…`: signed in; trade the code at `POST /auth/external/exchange`.
   - `/login/external?pending=…`: a step is left (link with a password, the invite PIN, create or link, or a refusal). `GET /auth/external/pending/{handle}` says which (`outcome`, `refusal`, `message`, `canCreateAccount`, `providerName`, `email`), and `POST /auth/external/complete { handle, createAccount?, invitePin?, linkEmail?, linkPassword? }` takes the answer (`AuthStrict`): `SignedIn` with a `code`, or the next step. A handle lives ten minutes and dies after five wrong passwords or PINs.
   - `/login/external?error=…`: it ended at once. Codes: `provider_unavailable`, `discovery_failed`, `access_denied`, `provider_error`, `state_expired`, `token_exchange_failed`, `id_token_invalid`, `missing_subject`.
3. **Linking from Account settings**: `POST /api/auth/oidc/{slug}/link { returnTo? }` (signed in) returns `{ url }` for the page to open. The same callback then adds the provider to that account and answers 302 to `/account?linked={slug}` or `/account?linkError=…` (the codes above, plus `link_conflict` when the provider account already belongs to another account here, and `link_refused` for a group or domain limit).

The id token must pass: signature against the provider's published keys (RS, PS and ES algorithms only, never `none` or a shared secret; a key it has not seen triggers one key refresh, at most every 30 seconds), `iss` equal to the issuer entered, `aud` equal to the client id (and `azp` when there are several), expiry (two minutes of skew), and the `nonce` of this sign-in. Email, name and groups come from the claims named on the provider; when the token has no email, or no groups while a group is required, the user-info endpoint is asked (only for the same `sub`). The admin's **Test** button (`POST /api/admin/auth-providers/{id}/test`) fetches the discovery document and the keys and reports the endpoints and what to fix.

## The provider's address is admin-supplied

The issuer is typed by an admin and is often on the LAN, so private addresses are allowed here, unlike the image downloader's guard (`docs/artwork-image-cache.md`). What limits it: http and https only; **redirects are never followed** (a 3xx is an error); 10 seconds in all and 1 MB per answer; the discovery document must name the issuer entered, and none of its endpoints may drop from https to http (a secret would cross the network unprotected); an http issuer is allowed and logged as a warning. Secrets, tokens and codes are never logged, and the saved secret is read only to trade a code.

## Not built yet

The web admin page and login buttons, the TV pairing code flow, the Android phone return, and LDAP. See issue #294.

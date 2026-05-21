# Email

SMTP-based outbound email subsystem. Powers password reset, admin-initiated invitations, request-fulfilled notifications, and an admin test-send button.

## Configuration

All email config lives on `ServerSetting`. Admin edits through **Admin → System Settings → Email**.

| Column | Purpose |
| --- | --- |
| `EmailEnabled` | Master switch. When off, queued sends short-circuit and the "Forgot password?" link is hidden on the login page. |
| `SmtpHost`, `SmtpPort` | Standard SMTP endpoint. |
| `SmtpUseStartTls` / `SmtpUseImplicitSsl` | Mutually exclusive TLS modes (implicit SSL wins if both set). |
| `SmtpUsername` | Auth username (optional — anonymous SMTP works if both creds are blank). |
| `SmtpPasswordCiphertext` | DataProtection-encrypted password. **Never leaves the server**; the VM masks it as a boolean `smtpPasswordIsSet`. |
| `SmtpFromAddress` / `SmtpFromDisplayName` | From header. |
| `EmailPublicBaseUrl` | Used to build absolute links in emails. Required for production — background workers have no HTTP context to fall back to. |

**Secret storage.** SMTP password is encrypted via `IDataProtector` (purpose `"Vora.Email.SmtpPassword.v1"`). Keys persist to `StoragePaths:DataProtection` (default `<base>/DataProtectionKeys`). **Mount this directory as a Docker volume** — otherwise keys roll on every container rebuild and the saved SMTP password becomes undecryptable.

## Architecture

| Type | Lifetime | File |
| --- | --- | --- |
| `IEmailService` / `EmailService` | Scoped | `Vora.Application/Email/IEmailService.cs` |
| `IEmailTransport` / `SmtpEmailTransport` | Scoped | abstraction in Application, MailKit impl in `Vora.Infrastructure/Email/` |
| `IEmailTemplateRenderer` / `EmailTemplateRenderer` | Scoped | merges built-in defaults with admin overrides, does HTML-encoded `{{var}}` substitution, strips CR/LF from subjects |
| `IEmailDispatchQueue` / `EmailDispatchQueue` | Singleton | bounded `Channel<QueuedEmail>` (capacity 256, blocks on full) |
| `EmailDispatchWorker` | Singleton (`BackgroundService`) | drains the queue, retries with backoff `1s → 5s → 30s`, writes to `EmailDeliveryLog` |
| `IEmailSecretProtector` / `DataProtectionEmailSecretProtector` | Singleton | `IDataProtector` wrapper |

`IEmailService.SendAsync` is the normal path — renders, writes a `Queued` log row, enqueues. `SendImmediateAsync` is for the admin test button — synchronous, returns the actual send result.

All DI registration lives in `AddVoraEmail(IConfiguration)` inside `ServiceRegistrationExtensions`. The worker is registered in `AddVoraWorkers`. The repositories in `AddVoraRepositories`.

## Templates

Four built-in keys (`Vora.Domain.Enums.EmailTemplateKey`):

| Key | Sent by |
| --- | --- |
| `PasswordReset` | `AuthManager.RequestPasswordResetAsync` |
| `AdminInvite` | `InvitationManager.CreateInvitationAsync` |
| `RequestAvailable` | `RequestNotificationService.NotifyRequestAvailableAsync` (hooked into `RequestManager.ResolveRequestAsync`) |
| `TestEmail` | `EmailSettingsManager.SendTestAsync` |

Each template ships as three embedded resources under `Vora.Application/Email/Templates/`: `<Key>.subject.txt`, `<Key>.html`, `<Key>.txt`. All three must be marked `<EmbeddedResource>` in `Vora.Application.csproj`.

The `EmailTemplate` entity stores per-key admin overrides (`SubjectOverride`, `HtmlBodyOverride`, `TextBodyOverride`). Any non-null override replaces the built-in field. Edit via **Admin → System Settings → Email → Templates → Edit**.

### Variables

`{{variableName}}` substitution. Variables per template are declared in `EmailTemplateVariables.cs` (the catalog feeds both the renderer and the admin editor's sidebar). HTML bodies get HTML-encoded values; text bodies pass through verbatim; subjects strip CR/LF and clamp to 256 chars.

### Adding a new template

1. Add the enum value to `EmailTemplateKey`.
2. Add three files under `Vora.Application/Email/Templates/`: `<Key>.subject.txt`, `<Key>.html`, `<Key>.txt`.
3. Mark them `<EmbeddedResource>` in `Vora.Application.csproj`.
4. Add a variable list entry in `EmailTemplateVariables.Catalog`.
5. Add a display name + description to `EmailTemplateManager.Metadata`.
6. Send from wherever via `IEmailService.SendAsync(new EmailMessage { TemplateKey = ..., ToAddress = ..., Variables = {…} })`.

## Feature gate

`FeatureGate.Email` reads `ServerSetting.EmailEnabled`. Admin settings/test/templates/log endpoints are intentionally **not** gated — admins need to configure email even when it's off. User-facing flows that depend on email enforce the check themselves (forgot-password silently no-ops, invitation creation returns 400 with explanation, request notifications skip).

## Use case wiring

**Password reset** — `AuthManager.RequestPasswordResetAsync`. Throttled to 3/hour/email via `IMemoryCache`. Tokens are 32 random bytes, base64url, SHA-256 hashed before storage in `PasswordResetTicket`. Lifetime 60 minutes. On successful reset, all outstanding tickets for that user are deleted.

**Admin invitation** — `InvitationManager.CreateInvitationAsync`. Per-email `InvitationTicket` with SHA-256 hashed token. Default 7 day lifetime (max 60). Sending a new invite for an email with an outstanding one invalidates the old one. Accepting an invite **bypasses** `RegistrationMode.Disabled` — the link is an explicit per-email exemption.

**Request fulfilled** — `RequestNotificationService.NotifyRequestAvailableAsync`, called from `RequestManager.ResolveRequestAsync` (the single chokepoint where status transitions `Processing → Available`). Iterates `request.Requesters`, skips anyone with `NotifiedAt != null` (per-requester to avoid double-sends on re-resolves), respects the per-user opt-out (`User.EmailNotifyOnRequestAvailable`, default true, surfaced on `AccountSettingsPage`).

## Delivery log

`EmailDeliveryLog` row per send: TemplateKey, ToAddress, Subject, Status (`Queued`/`Sent`/`Failed`/`Dropped`), AttemptCount, ErrorMessage (truncated to 512), CreatedAt, SentAt. Visible in the admin Email tab's "Recent activity" table. `IEmailDeliveryLogRepository.PruneOldAsync(keepCount)` available for housekeeping.

## Frontend services

- `src/api/System/emailAdminService.ts` — admin Email tab (settings, test, templates, log)
- `src/api/Auth/invitationsAdminService.ts` — admin Invitations page
- `src/api/Auth/authService.ts` — adds `requestPasswordReset`, `confirmPasswordReset`, `validateInvitation`; threads `inviteToken` through `register`/`registerOnServer`

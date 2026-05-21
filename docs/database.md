# Database

PostgreSQL with the `vector` extension, accessed via EF Core.

## DbContext

- **`Vora.Infrastructure/Persistence/VoraDbContext.cs`** is the main context. It's split into partials and per-aggregate `Configure*` helpers (`ConfigureMedia`, `ConfigureCollections`, `ConfigureClientDevices`, `ConfigurePlaylists`, …). When adding entities, follow the same pattern: a new `Configure<Aggregate>` method called from `OnModelCreating`.
- `DbSet<T>` properties are grouped by aggregate at the top of the class.
- Be aware of **vector columns** when modifying entities or writing queries — they're used for embeddings on media items.

## Migrations

- Migrations live in `/src/Vora.Infrastructure/Migrations`.
- Create them from the Visual Studio **Package Manager Console**:
  - `add-migration FooNameOfMigration`
  - `update-database`
- **Never** edit a checked-in migration. If you need to fix it, add a new one.

## Column constraints to remember

These are easy to overflow accidentally — note the limit when sending data from the frontend or external services:

| Entity | Property | Max length |
| --- | --- | --- |
| `ClientDevice.DeviceId` | unique device UUID | 128 |
| `ClientDevice.ClientName` | sent via `X-Vora-Client` header | 64 |
| `ClientDevice.DeviceName` | sent via `X-Vora-Device` header | 128 |
| `ClientDevice.DeviceType` | sent via `X-Vora-Device-Type` header | 32 |
| `ClientDevice.OperatingSystem` | sent via `X-Vora-OS` header (use a parsed short name, not raw `navigator.userAgent`) | 64 |
| `ClientDevice.LastIpAddress` | IPv4/IPv6 | 45 |
| `ClientDevice.Location` | reverse-geo string | 128 |
| `Media.Title` / `SortTitle` / `OriginalTitle` | | 500 |
| `Media.OriginalLanguage` | ISO code | 8 |
| `Media.Edition` | | 64 |
| `Media.Status` | | 32 |
| `Media.HomePage` | URL | 1024 |
| `Media.ContentRating` | | 32 |
| `Media.TmdbId` / `ImdbId` / `TvdbId` | external IDs | 64 |
| `ServerSetting.AdminThemeId` | active admin theme id; falls back to `"vora-default"` if the persisted id no longer resolves in `IThemeRegistry` | (default 64) |
| `ServerSetting.SmtpHost` / `SmtpUsername` / `SmtpFromAddress` | SMTP config (see `docs/email.md`) | 256 |
| `ServerSetting.SmtpFromDisplayName` | display name in From header | 128 |
| `ServerSetting.SmtpPasswordCiphertext` | DataProtection-encrypted SMTP password | `text` |
| `ServerSetting.EmailPublicBaseUrl` | base URL for absolute links in emails | 512 |
| `EmailTemplate.Key` | string-converted `EmailTemplateKey` enum, primary key | 64 |
| `EmailTemplate.SubjectOverride` | admin-edited subject override | 256 |
| `EmailTemplate.HtmlBodyOverride` / `TextBodyOverride` | admin-edited body overrides | `text` |
| `EmailDeliveryLog.TemplateKey` / `Status` | string-converted enums | 64 / 16 |
| `EmailDeliveryLog.ToAddress` / `Subject` | recipient + rendered subject | 256 |
| `EmailDeliveryLog.ErrorMessage` | failure detail (truncated to 512 in the VM) | 2048 |
| `PasswordResetTicket.TokenHash` | SHA-256 of the reset token (hex, lowercased) | 128 |
| `InvitationTicket.Email` | invited email address | 256 |
| `InvitationTicket.TokenHash` | SHA-256 of the invite token (hex, lowercased) | 128 |
| `RegistrationTicket.SecretCode` | legacy 3-word shared invite code | 128 |

When auditing column lengths, look at `ConfigureXxx` helpers in `VoraDbContext.cs` — those have the truth.

## Test data / seeds

The DbContext includes API key seeds for testing IPTV / metadata providers. Don't remove them when refactoring; the user keeps them in for testing and removes them manually before release.

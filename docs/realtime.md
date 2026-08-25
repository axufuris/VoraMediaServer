# Real-time (SignalR)

## Hub

- **`Vora.Api/Hubs/VoraHub.cs`** — the SignalR `Hub`. Mapped at `/hubs/Vora`.
- **`Vora.Api/Hubs/SignalRClientNotifier.cs`** — implements `Vora.Application.Analysis.IClientNotifier`. Every push notification from the server flows through this class.

## Adding a new real-time event

1. Add a method to `IClientNotifier` in `Vora.Application/Analysis/`. Use an `Async` suffix and document the arguments via parameter names (no XML doc).
2. Implement it in `SignalRClientNotifier`.
3. Call the manager method that wraps the notifier (don't call the hub directly from endpoints).
4. On the frontend, subscribe via the `useSignalREvent` hook (see `src/hooks/useSignalREvent.ts`):

```ts
useSignalREvent("LibraryUpdated", useCallback(() => {
    fetchData(true);
}, [fetchData]));
```

The hook handles connect/disconnect and re-subscribes on remount automatically.

## Naming

Events use PascalCase verbs in past tense matching backend method names: `LibraryUpdated`, `MediaItemUpdated`, `DvrSessionsUpdated`, etc.

## Audience scoping

Most notifications go to `Clients.All`. Two patterns to be aware of:

- **Admin-scoped events** target `Clients.Group("admins")` — connected admins are added to that group in `VoraHub.OnConnectedAsync` based on the `IsAdmin` claim. Used by `AdminAlert`, `AdminAlertUnreadChanged`, `LogEntryBatch`, `BackupCreated`, and `BackupRestored` so only admins receive these.
- **`AdminThemeChanged`** goes to `Clients.All` (not the `admins` group) because `ThemeProvider` mounts at the app root and any authenticated session — admin or not — needs to re-apply CSS variables when the server theme changes. The payload is the new theme id (string).
- **Profile-scoped events** target `Clients.Group(VoraHub.ProfileGroupName(profileId))` — sessions join their profile group on connect. Used by `MusicMixesUpdated`, `RadioPrefsUpdated`, the profile-targeted `ClientTemplateConfigurationChanged`, and `UserMediaStateUpdated`. The last fires when a profile's own watch state changes — `StreamManager.StopSessionAsync` (resume position finalized) and `UserMediaStateManager.SetMediaPlayedStateAsync` (manual played toggle) — so the Home "Continue Watching" rail live-refreshes on the device that watched and any other device on the same profile. Payload is the profile id (string); Home subscribers ignore the arg and just refetch. Not fired per-ping (heartbeats write resume position but would be far too chatty to notify on).

## Coalesced admin streams

Some admin-only events would be too chatty if sent per-occurrence:

- **`LogEntryBatch`** — server log entries. `LogBroadcastHostedService` (`Vora.Application/Logging/`) windows entries into ~150 ms / 200-entry batches and pushes the batch as one payload. The admin Logs page consumes this via `useSignalREvent<LogEntryVM[]>('LogEntryBatch', ...)`. See `docs/logs.md`.
- **`BackupCreated` / `BackupRestored`** — fired once per operation by `BackupManager`. Payloads: a file name (string) and `{ fileName, sectionKeys: string[] }` respectively. The admin Backups page subscribes to refresh the list and invalidate caches. See `docs/backups.md`.

## Frontend client

`@microsoft/signalr` connects against `/hubs/Vora` on the active server. The connection is managed centrally so individual `useSignalREvent` calls share one socket. Don't open a second connection from a component.

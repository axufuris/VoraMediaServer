# Media webhooks (Radarr, Sonarr, Lidarr, or anything that can send one)

Radarr, Sonarr and Lidarr can tell Vora the moment a file is imported, upgraded, renamed or deleted, through the **Webhook** connection each has under Settings → Connect, and so can any other system that sends a webhook. Vora ingests just that file, or moves the deleted one to Media Trash, without waiting for the folder watcher. It also lets a library's real-time watching be switched off, which matters on a network share where polling the whole tree is the expensive part.

The admin page is **Admin → Library → Library Updates** (`docs/frontend-admin.md`).

## Connections and templates

- A **connection** is one thing that sends webhooks: a name, its own address and secret, its own path mappings, on or off, whether it handles deletes, and the settings of its template. An admin adds as many as they like, so two Radarrs (4K and HD, each with its own mount) are two connections. They are rows of `WebhookConnections` (`WebhookConnection`), like Request Servers.
- A **template** is a `MediaWebhook` plugin (`IMediaWebhookProvider`): it knows one source's payload. Built in: `radarr_webhook`, `sonarr_webhook`, `lidarr_webhook` and **`custom_webhook`**, for any other system. A connection names its template (`ProviderId`) and never changes it.

Vora owns everything the same for every source: the endpoint, the secret, path mapping, choosing the library, checking the disk and queueing. A template only turns a payload into file changes.

## The plugin contract

`IMediaWebhookProvider` (`Vora.Plugins/Interfaces`) extends `IVoraPlugin` (`Type` is `"MediaWebhook"`; it has no plugin-level settings):

```csharp
string DefaultConnectionName { get; }
string SetupInstructions { get; }                       // steps, separated by \n, shown beside the address
string? RequestServerProviderId => null;                // "radarr_requester": offer "fill in from a Request Server"
IEnumerable<MediaWebhookSettingDefinition> GetConnectionSettingDefinitions();   // the connection's own fields
LibraryKind? ResolveLibraryKind(IReadOnlyDictionary<string, string> settings);  // which libraries its files belong to
MediaWebhookParseResult Parse(string body, IReadOnlyDictionary<string, string> settings);
```

`Parse` returns the event name, the instance name, whether it was a `Test`, and a list of `MediaWebhookChange`: `Added(path)`, `Removed(path)`, `FolderRemoved(path)` or `ScanLibrary`. It throws `JsonException` for a body that isn't its format. It does no I/O and never decides what to ingest or delete. `MediaWebhookSettingDefinition` is `Key`, `Label`, `Type` (`text`, `textarea`, `select`, `library`), `DefaultValue`, `Description`, `Placeholder`, `Required`, `Options` (`value` or `value=Label`) and `ShowWhen` (`key=value`: shown only while that setting has that value). A `library` field holds a library id. Add the interface to `PluginProviderInterfaces` (done), so built-in and external templates are discovered. `ArrWebhookProviderBase` carries what the three apps share.

## The Custom template

For a system with no template. The connection says where to read:

| Setting | Meaning (default) |
|---|---|
| `action` | `ingest` the file named in the payload (default) or `scan` a library |
| `library_type` | for `ingest`: Movies, TV shows, Music or Home videos |
| `scan_library` | for `scan`: the library to scan (every matching event queues a scan; the queue folds repeats) |
| `event_path` | where the event name is in the JSON (`event`). Empty: every request counts as added |
| `added_events`, `removed_events`, `folder_removed_events`, `test_events` | comma lists of event names (`added`, `removed`, `folder_removed`, `test`), any case |
| `file_path` | where the file's full path is (`path`) |
| `also_removed_path` | paths also treated as removed on an added event, such as `deletedFiles[].path`; Vora checks each is really gone first |
| `folder_path` | where the folder path is, read on a folder removed event (`folder`) |

A path is dotted names into the JSON (`movieFile.path`, case ignored), `[]` after a list to read every item (`episodeFiles[].path`), and several paths separated by commas (`JsonValuePaths`). The defaults suit a script posting `{"event":"added","path":"/media/movies/Film/Film.mkv"}`. It only works for a system that puts a file path (or folder) in the payload; one that sends only titles can still use `scan`.

## The endpoint

`POST` or `PUT /api/webhooks/{connectionId}` (`MediaWebhookEndpoints`), the id being the connection's own unguessable id. It is anonymous at the framework level and guarded by the connection's secret.

- **The secret** is accepted any of three ways, so every version of the apps can use one: the **password** (or the username) of HTTP Basic auth, which is the Webhook connection's own Username and Password; an `X-Api-Key` header; or `?token=`. Basic or the header is better, since a query string ends up in access logs. The comparison is constant time (`CryptographicOperations.FixedTimeEquals` over SHA-256 of both).
- **Answers:** `202` accepted; `401` wrong or missing secret; `404` no such connection, it is switched off, its template is gone, or its secret cannot be read with this server's keys (a restore without the DataProtection keys: regenerate it), so a server with nothing set up shows nothing; `400` not a payload its template understands; `413` over 1 MB. A `Test` and an event that changes no files are `202` too, so the app never flags a failure.
- **Limits:** 600 requests a minute per client address (`webhook-receive`), since a season pack is one webhook per episode, and a 1 MB body.
- **It answers at once.** The work is queued to `MediaWebhookProcessor` and done in the background.

## The secret

Made by Vora (256 random bits) when a connection is created and shown with its address. It is stored as DataProtection ciphertext (`SecretCiphertext`, `DataProtectionWebhookSecretProtector`, purpose `Vora.Webhooks.ConnectionSecret.v1`), as the SMTP and IPTV passwords are, so the connection's page can show it again; the list never carries it. Regenerate replaces it. A backup carries the ciphertext, which opens only with the matching DataProtection keys (`docs/backups.md`).

## Admin endpoints

`AdminOnly`, under `/api/admin/webhooks`:

- `GET /providers`: the templates (name, description, default name, setup steps, library kind, whether it can fill in from a Request Server, its settings).
- `GET /connections`, `GET /connections/{id}` (adds the secret), `POST /connections` (`201`, returns the secret once), `PUT /connections/{id}` (name, on/off, handle deletes, path mappings, settings; `400 { error }` with the reason), `POST /connections/{id}/secret`, `DELETE /connections/{id}`. Defaults fill the settings a request leaves out; required settings are checked only while the field is shown (`ShowWhen`).
- `GET /deliveries?limit=&connectionId=`: the last 200 in memory, newest first.
- `POST /preview`: a pasted payload and the unsaved fields go in, what Vora would do comes out (event, each change, where it maps, the library, whether it is on disk), and nothing is queued.
- `GET /request-servers` and `GET /request-servers/{id}/mappings`: see **Filling in from a Request Server**.

## Setting it up in the app

Settings → Connect → + → **Webhook**:

| Field | Value |
|---|---|
| URL | the connection's address, `http://<vora address>:<port>/api/webhooks/<id>`, one the app can reach |
| Method | POST |
| Username | anything, such as `vora` |
| Password | the secret |

Tick the import and upgrade triggers (Radarr and Sonarr call them **On File Import** and **On File Upgrade**; Sonarr also has **On Import Complete**, and both import shapes are handled), the rename trigger, and the delete triggers (a movie, series, artist or album being deleted, and a movie or episode file being deleted). Lidarr also has a retag trigger. The other triggers send events Vora ignores. **Test** sends a `Test`, which Vora answers and lists.

## What each event does

| Event | Vora does |
|---|---|
| Radarr / Sonarr / Lidarr `Download` | ingests the new file(s); for each file in `deletedFiles` that the import replaced, checks and removes it |
| `Rename` | ingests the new path, checks and removes the previous one |
| Lidarr `Retag` | ingests the file again |
| `MovieFileDelete`, `EpisodeFileDelete` | checks and removes that file |
| `MovieDelete`, `SeriesDelete`, `ArtistDelete`, `AlbumDelete` with `deletedFiles` true | sweeps the title's folder for files that are gone (a deleted album sweeps the artist's folder) |
| the same, `deletedFiles` false | nothing: the files are still there |
| `Test` | answers, and lists it |
| a Custom event | by its settings above |
| anything else | nothing |

Sonarr's **On Import Complete** arrives with `eventType` `Download` but a list, `episodeFiles`, where **On File Import** has one `episodeFile`. The parser reads both. A path both added and removed in one event (an upgrade in place) is only added. A connection with **Move deleted files to Media Trash** off (`HandleDeletes`) still ingests but ignores every removal and says so in its deliveries.

## Finding the file

The apps report paths as *they* see the disk, such as `/tv/Show/Season 1/e1.mkv`, which may be `/media/tv/...` to Vora. `MediaWebhookPathResolver` turns one into a path inside a library folder of the connection's kind, in this order:

1. **A path mapping** from the connection's `PathMappings`, one per line, `from => to` (`/tv => /media/tv`, or `D:\Media\TV => /media/tv`; a line with no arrow is refused when saving). The longest `from` that starts the path wins, folder by folder, ignoring case. The result must be inside a library folder of the kind, or the delivery says which mapping put it where.
2. **Already inside a library folder.** Both see the same paths.
3. **By name.** Leading folders are dropped one at a time, as few as possible, until the first remaining folder exists under a library folder of that kind (or Vora has media filed under it, which is how a deleted title is still found). `/data/media/tv/Show/S1/e1.mkv` finds `/media/tv/Show/S1/e1.mkv` with no mapping at all.

A path with a `..` folder in it is refused. A path nothing matches is listed with "add a path mapping", and nothing is queued. When a mapped or already-matching path falls under two library folders of the kind, the longest folder wins.

### Filling in from a Request Server

A Radarr or Sonarr template offers **Fill in from a Request Server** (`RequestServerProviderId`: `radarr_requester`, `sonarr_requester`), for the servers already under Request Servers. `GET /request-servers/{id}/mappings` asks that server for its root folders (`GET /api/v3/rootfolder`, with its API key, 10 seconds, `ArrRootFolderClient`) and suggests a mapping per root (`MediaWebhookMappingSuggester`): to the library folder of the template's kind, or a folder above it, whose ending shares the most folder names with the root (any case), so a root `/movies` is matched to `/media/movies` when the libraries are `/media/movies/1080p` and `/media/movies/4K`, and `/movies/1080p` to `/media/movies/1080p`; else to the only library folder there is; else the root is listed as unmatched. It is a suggestion: Try a sample payload shows where a path really lands. The page fills the name and the mappings box; if the server can't be reached the answer carries the reason in `error` and nothing is filled. Lidarr has no request server, so none is offered.

## Adding a file

`MediaWebhookProcessor` checks the file is a media file the library holds, that the library's exclude filters and the server's ignored folders don't skip it, then waits for it to exist: now, and after 2, 5, 15, 30, 60 and 120 seconds, since an import can be visible a moment late on a network share. A file that never shows up is listed with a hint, and a warning is logged. Then it queues the same single-file ingest the folder watcher does (`QueueScanNewFile`, or `QueueScanNewMusicFile` for music), so it needs no watcher and no scan of the library. The same file reported twice within a minute is queued once (Download plus Import Complete). A Custom `scan` connection queues `QueueScanLibrary` for its library instead; that task folds repeats (`library-scan:{id}`).

## Deleting a file

A webhook never deletes anything because it says so. It only prompts Vora to look.

- **A file:** wait 5 seconds, as the watcher does, since an upgrade deletes and writes close together. If the file **exists**, it is kept and the delivery says so. If the library's folder itself **cannot be reached** (an unmounted share looks like every file deleted), nothing is removed. Otherwise `QueueRemoveOrphanedMedia` runs, which checks `File.Exists` **again** and then does the soft delete: the part is dropped and, when it was the item's last, the item gets `MissingSince`, so it lands in **Media Trash** (`docs/scanning-and-tasks.md`, Soft-delete & Media Trash) and can be restored.
- **A folder:** refused if it is a library folder itself. Otherwise the same two guards, then `QueueRemoveMissingMediaUnder` reads every file Vora has under the folder (`MediaRepository.MarkMediaMissingUnderPathAsync`, matched with a trailing separator so `Show` never reaches `Show Two`) and soft-deletes **only those that are not on disk**. A folder that still exists loses only its missing files. The task is saved with the other queued work, so it survives a restart.

## The delivery log

`IMediaWebhookLog` keeps the last 200 results in memory, each against its connection (time, connection, instance, event, path, outcome, whether it worked), for the admin list and each connection's "last delivery". It is not stored: a restart clears it. Webhooks are not replayed after a restart either; a file added while Vora was down is the watcher's reconcile or a manual scan's.

## Tests

`ArrWebhookProviderTests` (every event of the three apps, case, both Sonarr import shapes), `CustomWebhookProviderTests` (the defaults, event matching, nested and list paths, scan, `JsonValuePaths`), `MediaWebhookPathResolverTests` (resolution, mapping lines, the suggester), `MediaWebhookProcessorTests` (real files in a temp folder: ingest, a file that never appears, exclusions, duplicates, deletes, an unreachable library folder, upgrades, folders, handle deletes off, scan), `MediaWebhookManagerTests` (the secret, off, unknown and unreadable connections, CRUD and validation, two connections of one template, preview, Request Server suggestions), `WebhookConnectionInfrastructureTests`, `MediaMissingUnderPathTests` and `PostgresMissingUnderPathTests`, `MediaWebhookEndpointTests`, the backup coverage test and the migration test (`PostgresMigrationHistoryTests`).

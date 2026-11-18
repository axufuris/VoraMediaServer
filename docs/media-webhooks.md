# Media webhooks (Radarr, Sonarr, Lidarr)

Radarr, Sonarr and Lidarr can tell Vora the moment a file is imported, upgraded, renamed or deleted, through the **Webhook** connection each has under Settings → Connect. Vora ingests just that file, or moves the deleted one to Media Trash, without waiting for the folder watcher. It also lets a library's real-time watching be switched off, which matters on a network share where polling the whole tree is the expensive part.

A webhook is a plugin type, `MediaWebhook`, so another source can be added the way the three built-ins are. Vora owns everything that is the same for every source (the endpoint, the secret, path mapping, picking the library, checking the disk, queueing). A provider only turns one source's payload into a list of file changes.

## The plugin contract

`IMediaWebhookProvider` (`Vora.Plugins/Interfaces`) adds one method to `IVoraPlugin`:

```csharp
MediaWebhookParseResult Parse(string body);
```

It returns the source's event name, its instance name, whether it was a `Test`, and a list of `MediaWebhookChange`: `Added(path)`, `Removed(path)` or `FolderRemoved(path)`. It throws `JsonException` for a body that isn't its format. It does no I/O and never decides what to ingest or delete.

- `Type` is `"MediaWebhook"`. `SupportedLibraryKinds` names the one kind of library its files belong to (`Movie`, `TvShow`, `Music`, `HomeVideo`); that picks which libraries a path may land in.
- It declares two settings, named by `MediaWebhookSettingKeys`: **`secret`** (`password`, `Required`) and **`path_mappings`** (`textarea`). `ArrWebhookProviderBase` declares both and the helpers the three built-ins share.
- Add the interface to `PluginProviderInterfaces` in `PluginLoaderExtensions` (done), so built-in and external providers are discovered.

The built-ins are `radarr_webhook`, `sonarr_webhook` and `lidarr_webhook`, one folder each under `Providers/`. They are plugins, not server settings, because their settings live in the plugin store: no migration, any number of providers, and `Vora__PluginSettings__radarr_webhook__secret` seeds a secret from the environment like any other plugin setting (`docs/plugins.md`).

## The endpoint

`POST` or `PUT /api/webhooks/{providerId}` (`MediaWebhookEndpoints`). It is anonymous at the framework level and guarded by the provider's secret.

- **The secret** is accepted any of three ways, so every version of the apps can use one: the **password** (or the username) of HTTP Basic auth, which is the Webhook connection's own Username and Password; an `X-Api-Key` header; or `?token=`. Basic or the header is better, since a query string ends up in access logs. The comparison is constant time (`CryptographicOperations.FixedTimeEquals` over SHA-256 of both).
- **Answers:** `202` accepted; `401` wrong or missing secret; `404` no such provider, the provider has no secret yet, or it is switched off (`is_enabled` is `false`), so an unconfigured server shows nothing; `400` not a payload the provider understands; `413` over 1 MB. A `Test` and an event that changes no files (Grab, Health, a series added) are `202` too, so the app never flags a failure.
- **Limits:** 600 requests a minute per client address (`webhook-receive`), since a season pack is one webhook per episode, and a 1 MB body.
- **It answers at once.** The work is queued to `MediaWebhookProcessor` and done in the background.

## Setting it up

Admin endpoints (`AdminOnly`, `/api/admin/webhooks`): `GET /` lists the providers (path, whether a secret is set, whether it is on; never a secret), `GET /deliveries?limit=` the recent deliveries, and `POST /{providerId}/secret` generates a random 256-bit secret, stores it and returns it once.

In the app: Settings → Connect → + → **Webhook**.

| Field | Value |
|---|---|
| URL | `http://<vora address>:<port>/api/webhooks/radarr_webhook` (or `sonarr_webhook`, `lidarr_webhook`), an address the app can reach |
| Method | POST |
| Username | anything, such as `vora` |
| Password | the secret |

Tick the import and upgrade triggers (Radarr and Sonarr call them **On File Import** and **On File Upgrade**; Sonarr also has **On Import Complete**, and both import shapes are handled), the rename trigger, and the delete triggers (a movie, series, artist or album being deleted, and a movie or episode file being deleted). Lidarr also has a retag trigger. The other triggers send events Vora ignores. **Test** sends a `Test`, which Vora answers and lists.

## What each event does

| Event | Vora does |
|---|---|
| Radarr / Sonarr `Download`, Lidarr `Download` | ingests the new file(s); for each file in `deletedFiles` that the import replaced, checks and removes it |
| `Rename` | ingests the new path, checks and removes the previous one |
| Lidarr `Retag` | ingests the file again |
| `MovieFileDelete`, `EpisodeFileDelete` | checks and removes that file |
| `MovieDelete`, `SeriesDelete`, `ArtistDelete`, `AlbumDelete` with `deletedFiles` true | sweeps the title's folder for files that are gone (a deleted album sweeps the artist's folder) |
| the same, `deletedFiles` false | nothing: the files are still there |
| `Test` | answers, and lists it |
| anything else | nothing |

Sonarr's **On Import Complete** arrives with `eventType` `Download` but a list, `episodeFiles`, where **On File Import** has one `episodeFile`. The parser reads both. A path that is both added and removed in one event (an upgrade in place) is only added.

## Finding the file

The apps report paths as *they* see the disk, such as `/tv/Show/Season 1/e1.mkv`, which may be `/media/tv/...` to Vora. `MediaWebhookPathResolver` turns one into a path inside a library folder, in this order:

1. **A path mapping** from the provider's `path_mappings`, one per line, `from => to` (`/tv => /media/tv`, or `D:\Media\TV => /media/tv`). The longest `from` that starts the path wins, folder by folder, ignoring case. The result must be inside a library folder of the provider's kind, or the delivery says which mapping put it where.
2. **Already inside a library folder.** Both see the same paths.
3. **By name.** Leading folders are dropped one at a time, as few as possible, until the first remaining folder exists under a library folder of that kind (or Vora has media filed under it, which is how a deleted title is still found). `/data/media/tv/Show/S1/e1.mkv` finds `/media/tv/Show/S1/e1.mkv` with no mapping at all.

A path with a `..` folder in it is refused. A path nothing matches is listed with "add a path mapping", and nothing is queued. When a mapped or already-matching path falls under two library folders of the kind, the longest folder wins.

## Adding a file

`MediaWebhookProcessor` checks the file is a media file the library holds, that the library's exclude filters and the server's ignored folders don't skip it, then waits for it to exist: now, and after 2, 5, 15, 30, 60 and 120 seconds, since an import can be visible a moment late on a network share. A file that never shows up is listed with a hint, and a warning is logged. Then it queues the same single-file ingest the folder watcher does (`QueueScanNewFile`, or `QueueScanNewMusicFile` for music), so it needs no watcher, no scan of the library, and nothing a watcher would have done differs. The same file reported twice within a minute is queued once (Download plus Import Complete).

## Deleting a file

A webhook never deletes anything because it says so. It only prompts Vora to look.

- **A file:** wait 5 seconds, as the watcher does, since an upgrade deletes and writes close together. If the file **exists**, it is kept and the delivery says so. If the library's folder itself **cannot be reached** (an unmounted share looks like every file deleted), nothing is removed. Otherwise `QueueRemoveOrphanedMedia` runs, which checks `File.Exists` **again** and then does the soft delete: the part is dropped and, when it was the item's last, the item gets `MissingSince`, so it lands in **Media Trash** (`docs/scanning-and-tasks.md`, Soft-delete & Media Trash) and can be restored.
- **A folder:** refused if it is a library folder itself. Otherwise the same two guards, then `QueueRemoveMissingMediaUnder` reads every file Vora has under the folder (`MediaRepository.MarkMediaMissingUnderPathAsync`, matched with a trailing separator so `Show` never reaches `Show Two`) and soft-deletes **only those that are not on disk**. A folder that still exists loses only its missing files. The task is saved with the other queued work, so it survives a restart.

## The delivery log

`IMediaWebhookLog` keeps the last 200 results in memory (time, source, instance, event, path, outcome, whether it worked) for the admin list. It is not stored: a restart clears it. Webhooks are not replayed after a restart either; a file added while Vora was down is the watcher's reconcile or a manual scan's.

## Tests

`ArrWebhookProviderTests` (every event of the three apps, case, both Sonarr import shapes), `MediaWebhookPathResolverTests`, `MediaWebhookProcessorTests` (real files in a temp folder: ingest, a file that never appears, exclusions, duplicates, a deleted file, a file still on disk, an unreachable library folder, an upgrade, a series folder, a whole library folder), `MediaWebhookManagerTests` (the secret, off, unknown, test, bad body), `MediaMissingUnderPathTests` and `MediaWebhookEndpointTests`.

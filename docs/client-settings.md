# Client settings: defaults, and an admin editing a profile

Issue #140. A profile's client settings are **its own preferences**. An admin can start a new profile on sensible ones and can change an existing profile's, but never lock them: the profile can change them again in Settings, and an admin who needs to force something uses the tools that do (`MaxRemoteStreamBitrateMbps` is the one server-enforced cap).

## What counts as a client setting

Two kinds. The first is covered up to **Per-device settings**, the second after it:

- **Per profile** (columns on `UserProfile`, read and written through the `/api/users/profiles/me/…` endpoints): playback behaviour (`PlaybackPreferencesVM`: auto-skip intros and credits, play the next episode automatically, where Play on a show starts, how a season's episodes are laid out, the minimum credit-scene gap), audio and subtitle preferences (`TrackPreferencesVM`), and the library sort (`LibrarySortVM`).
- **Per profile and device** (`ProfileDeviceSetting.PlaybackPrefs`, one JSON blob per device): the bitrate cap, the resolution cap, the audio-layout pick, the seek and volume steps, the UI sounds. The second half of this page.

Not covered on purpose: the audio-layout pick as a default (the raw pick is sent beside what the device detected, so a server-wide 7.1 would be direct-played on a stereo set), the theme (the server already has a live default, `DefaultClientTemplateId`, and each profile's own pick, `docs/design/client-templates.md`), the navigation blob (its library entries are keyed by an id that only exists on one install), the Home and Discover layouts (the admin already shapes them through smart lists and Discover rows), and anything a device keeps for itself (Desktop's local settings, the audio EQ, crossfade).

## One set of rules

The three groups are held as small values that carry their own validation, so a profile's own save, the defaults and an admin edit cannot disagree:

- `PlaybackBehaviour.Updated(PlaybackPreferencesVM)`: refuses an unknown up-next order or episode layout, keeps the stored value for a field the request leaves out (`autoPlayNext`, `upNextOrder`, `episodeLayout`), and clamps the credit-scene seconds to 0–600.
- `LibrarySortChoice.From(LibrarySortVM)`: refuses an unknown field, and a direction only counts with a field.
- `TrackPreferences.From(SaveTrackPreferencesRequest)`: refuses an unknown subtitle mode, turns a language name into its code (`Spanish` → `spa`), refuses a code over 16 characters. A language or mode left out is **Automatic** (stored as null), which follows the server.

`ProfileClientSettings` (extensions on `UserProfile`) and `ClientDefaultsManager` (on `ServerSetting`) only read and write those values. A save validates every group it was sent before writing any, so a bad group writes nothing.

## Defaults for new profiles

`ServerSettings` holds twelve typed columns, one per setting, named `NewProfile…` (`NewProfileAutoPlayNext`, `NewProfileAudioLanguage`, `NewProfileLibrarySort`, …). They start as exactly what a new `UserProfile` starts with, so nothing changes on a server nobody configures, and `PostgresMigrationHistoryTests` pins that for a database upgraded from before they existed.

`GET` and `PUT /api/settings/client-defaults` (admin only, `ClientSettingsVM` / `SaveClientSettingsRequest`) read and save them. The request holds up to three groups, `playback`, `tracks` and `librarySort`; a group left out is unchanged.

A default is **copied into a profile when the profile is created**, by `IClientDefaultsManager.ApplyToNewProfileAsync`, and never read again. That is the point: changing a default never overwrites a choice a person already made. The two places a profile is created call it:

- `AuthManager.AddNewUserAsync`: the first profile of every new account, whether the account came from registration, an invitation, an admin adding it (with or without a password), an external sign-in or the claim of the server.
- `UserManager.CreateManagedProfileAsync`: a profile added under an existing account.

A server-wide "apply to existing profiles" button is deliberately not offered: it would overwrite what people chose. Changing one profile is the admin edit below.

## An admin editing a profile

`GET` and `PUT /api/users/profiles/{profileId}/client-settings` (admin only, the same two view models) read and save one profile's settings through `ProfileClientSettingsManager`. A group left out is unchanged, every group sent is validated first, and the profile is written once. No security stamp changes and nobody is signed out or sent back to the profile picker. The profile's sessions get `ClientSettingsChanged`; the web re-reads its device settings at once and the profile's own groups the next time a page that uses them loads, and the Android, Roku, tvOS and iOS apps pick the values up the next time they open the screen or start playback.

## The web app

- Admin → Server → **Client Defaults** (`/admin/client-defaults`, `ClientDefaultsPage`) edits the defaults.
- Admin → **Users & Access**, **Client settings** on a profile's tile opens a page of its own, `/admin/users/:profileId/client-settings` (`ProfileClientSettingsPage`). It was a window once, and a window is too small as soon as a profile has more than a few devices. The **Profile** tab holds the three groups; the **Devices** tab holds the devices (below). `?tab=devices&device=<deviceId>` opens the Devices tab with that device's settings ready to edit, which is where **Settings** on the Authorized Devices page lands.
- Both use `components/Common/ClientSettings/ClientSettingsEditor`. The fields are shared with the user's own Settings (`PlaybackBehaviourFields`, `TrackPreferenceFields`, `LibrarySortFields`; the form values and their conversions are in `clientSettingsForm.ts`), so a field added there appears in all three places.

## Per-device settings

The blob holds up to eight keys: `bitrate` (Mbps, 0 = no cap), `maxResolution` (height, 0 = no cap), `maxAudioChannels`, `seekStepSeconds` (5, 10, 15, 30), `volumeStepPercent` (2, 5, 10), `soundOnClick`, `soundOnNavOpen`, `soundOnMove`. The web writes the first five and the TV apps all eight, and a client that saves rewrites the whole blob, so nothing an admin adds can sit inside it beyond those keys.

`DeviceSettings` is the validated value for the seven an admin can set (everything but the audio-layout pick), `DevicePlaybackBlob` reads and writes the blob (keys in any case, a bare number or broken JSON as nothing saved, other keys kept, `maxAudioChannels` kept).

### Defaults for devices: a live fallback

Seven `ServerSettings.DeviceDefault…` columns (bitrate, resolution, seek step, volume step, three sounds; they start as 0, 0, 10, 5, on, on, on). Unlike the new-profile defaults they are **never copied**: `GET /api/users/profiles/{profileId}/devices/{deviceId}/playback` answers each key the device's blob lacks from them (`IProfileDeviceSettingsManager.GetPlaybackAsync`), and a key the blob has wins, including a 0. A device that has never saved follows the defaults, and a change reaches it at its next read. A device that saves Settings pins every value its screen shows (the TV apps send all eight), so from then on it keeps its own.

They are saved with the rest of the defaults: `PUT /api/settings/client-defaults` takes a `device` group beside `playback`, `tracks` and `librarySort`, and answers with all four (`ClientDefaultsVM`). Saving the `device` group sends `ClientSettingsChanged` to every client, with no payload.

### An admin editing a profile's devices

- `GET /api/users/profiles/{profileId}/device-settings` (admin only) lists every device the profile has settings on or was last used on (`ProfileDeviceVM`: name, **nickname**, client, type, operating system, last connected, and the **settings it actually uses**, defaults filled in), most recently connected first. The nickname is the admin's own name for the device (`docs/auth-and-devices.md`); the web shows it first and the device's own name beneath it, so two SHIELDs in one house can be told apart.
- `PUT` on the same address takes `{ deviceIds, settings }`. `deviceIds` left out (or null) is every device in that list; a device the profile has not used is refused with 400 and nothing is written. The seven settings are validated first, every blob is written in one save, and the audio-layout pick and any other key in a blob are kept.
- Either save sends `ClientSettingsChanged` to the profile (payload: the profile id), as does the admin edit of the profile's own groups. Nothing about the access permissions or the security stamp changes.

### The web follows the server

The web used to treat `playback_prefs_<profile>_<device>` in localStorage as the truth and the server as a mirror, and the code that restored it from the server read a property the endpoint never returned, so it wrote `0`. Now it matches the TV apps: **the server is the copy**, and localStorage stays only because the player and the hardware scan read it synchronously.

- `usePlaybackPrefsSync` (used by `MainLayout`) reads the typed `GET …/playback` when the profile or server changes and on `ClientSettingsChanged` for the profile (or with no payload, for a change to the defaults), writes the five keys the readers know to the local key through `storePlaybackPrefs`, and announces a change. If the server cannot be read, the device keeps what it has.
- Settings → Playback saves to the server first. If that fails it says so and changes nothing here, instead of saying it saved. While the tab is open it follows a change the server pushes, until something on the tab has been edited.
- Settings → Providers sends only the Live TV preferences. It used to send the playback blob it had read from localStorage beside them, which would have undone an admin's change the device had not read yet.

The admin side: **Client Defaults** has a second card for the device defaults, and the Devices tab of a profile's **Client settings** page (`ProfileDevicesPanel`) is a table: the device, when it was last used, what it uses, and **Edit**, which opens that device's settings in its own row. Ticking devices (the header box ticks every one shown) offers **Set the same on these N…**, which sends exactly those device ids, starting from what the most recently used of them has now. From six devices there is a filter box; a selection only ever applies to the devices it is showing.

Not built: a way for the TV apps to listen for `ClientSettingsChanged` (they read the caps at every stream start, the seek step and sounds at launch or when Settings opens, so a change reaches them without it).

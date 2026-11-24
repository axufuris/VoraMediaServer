# Client settings: defaults for new profiles and an admin editing a profile

Issue #140. A profile's client settings are **its own preferences**. An admin can start a new profile on sensible ones and can change an existing profile's, but never lock them: the profile can change them again in Settings, and an admin who needs to force something uses the tools that do (`MaxRemoteStreamBitrateMbps` is the one server-enforced cap).

## What counts as a client setting

Two kinds, and this page's first half covers the first:

- **Per profile** (columns on `UserProfile`, read and written through the `/api/users/profiles/me/…` endpoints): playback behaviour (`PlaybackPreferencesVM`: auto-skip intros and credits, play the next episode automatically, where Play on a show starts, how a season's episodes are laid out, the minimum credit-scene gap), audio and subtitle preferences (`TrackPreferencesVM`), and the library sort (`LibrarySortVM`).
- **Per profile and device** (`ProfileDeviceSetting`, one JSON blob per device): bitrate, resolution and audio-layout caps, the seek and volume steps, the UI sounds. Not covered yet.

Not covered on purpose: the theme (the server already has a live default, `DefaultClientTemplateId`, and each profile's own pick, `docs/design/client-templates.md`), the navigation blob (its library entries are keyed by an id that only exists on one install), the Home and Discover layouts (the admin already shapes them through smart lists and Discover rows), and anything a device keeps for itself (Desktop's local settings, the audio EQ, crossfade).

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

`GET` and `PUT /api/users/profiles/{profileId}/client-settings` (admin only, the same two view models) read and save one profile's settings through `ProfileClientSettingsManager`. A group left out is unchanged, every group sent is validated first, and the profile is written once. The profile finds the new values the next time its client reads them: the web when a page that uses them loads, the Android, Roku, tvOS and iOS apps when they next open the screen or start playback. Nothing is pushed yet, and no security stamp changes, so nobody is signed out or sent back to the profile picker.

## The web app

- Admin → Server → **Client Defaults** (`/admin/client-defaults`, `ClientDefaultsPage`) edits the defaults.
- Admin → **Users & Access**, **Client settings** on a profile's tile (`ProfileClientSettingsModal`) edits that profile.
- Both use `components/Common/ClientSettings/ClientSettingsEditor`. The fields are shared with the user's own Settings (`PlaybackBehaviourFields`, `TrackPreferenceFields`, `LibrarySortFields`; the form values and their conversions are in `clientSettingsForm.ts`), so a field added there appears in all three places.

# Playlists & smart playlists

Vora has two playlist kinds. Both are profile-scoped and typed by `PlaylistMediaType`.

## `PlaylistMediaType`

```
Mixed = 0   // any media type, manual only
Music = 1
Movies = 2
Shows = 3
```

Both `Playlist` and `SmartPlaylist` carry this column. The frontend filters the Playlists page by tab using this value (tabs: `All` / `Music` / `Movies & Shows`). `Mixed` playlists only appear under `All`.

## Manual playlist (`Playlist`)

User-curated, ordered list of `MediaItem`s via `PlaylistItem` rows (`PlaylistItemId`, `Order`). The TPH join means a single playlist *can* hold tracks + movies + episodes, but in practice the `MediaType` is set at creation and the UI primarily adds matching items.

- Entity: `Vora.Domain/Entities/Playlists/Playlist.cs`
- Repo: `PlaylistRepository.cs` — recognizes `Track` items and uses album artwork as the fallback poster when projecting `PlaylistItemVM`.
- Manager: `IPlaylistManager` — `CreatePlaylistAsync` takes a `PlaylistMediaType`.
- Endpoint: `/api/playlists`.

## Smart playlist (`SmartPlaylist`)

Rule-driven, evaluated live on view. No `PlaylistItem` rows — items are computed each time `/api/smart-playlists/{id}/items` is called.

- Entity: `Vora.Domain/Entities/Playlists/SmartPlaylist.cs`
- Rule types: `Vora.Application/Media/SmartPlaylists/SmartPlaylistRules.cs`
- Repo / evaluator / manager: `Vora.Application/Media/SmartPlaylists/` + `Vora.Infrastructure/Persistence/Repositories/SmartPlaylist*.cs`
- Endpoints: `/api/smart-playlists` + `/{id}/items` + `/preview`

### Rule tree

Stored as JSON in `SmartPlaylist.RulesJson`:

```json
{
  "match": "All",                    // or "Any"
  "rules": [
    { "field": "Genre", "operator": "Contains", "value": "rock" },
    { "field": "PlayCount", "operator": "GreaterThan", "value": "10" }
  ],
  "groups": [
    {
      "match": "Any",
      "rules": [
        { "field": "Year", "operator": "Between", "value": "1990", "secondValue": "1999" }
      ]
    }
  ]
}
```

Nested groups are supported. The evaluator combines predicates with `AndAlso` / `OrElse` per `match`.

### Evaluator architecture

`SmartPlaylistEvaluator` dispatches by `MediaType`:

- **Music** — projects `Tracks` joined with `Album`/`Artist` into a flat `MusicRow` with profile-scoped subqueries for `PlayCount`, `LastPlayedAt`, `Liked`.
- **Movies** — projects `Movies` into `VideoRow` with subquery for `IsWatched` (`UserMediaStates`).
- **Shows** — projects `Episodes` joined with `Season.TvShow` into `VideoRow`.

For each rule, an `Expression<Func<TRow, bool>>` predicate is built dynamically (`System.Linq.Expressions`) per the field's kind (`string` / `int` / `decimal` / `date` / `bool` / `guid` / `StringCollection`). Predicates compose via a `ParameterExpression` rewriter so EF Core sees a single tree.

After filtering + sorting + limit, the evaluator returns track/movie/episode IDs, then re-fetches the full entities with their navigation includes.

### Fields available per type

| Field | Music | Movies | Shows |
| --- | :---: | :---: | :---: |
| `Title` | ✓ | ✓ | ✓ (episode title) |
| `Artist`, `AlbumTitle`, `AlbumArtist`, `IsCompilation`, `TrackNumber`, `DiscNumber`, `Liked`, `PlayCount` | ✓ | | |
| `ShowTitle`, `SeasonNumber`, `EpisodeNumber` | | | ✓ |
| `Genre` | ✓ (album.genre) | ✓ (any-of media genres) | ✓ (any-of show genres) |
| `ContentRating`, `DateAdded`, `LastPlayedAt`, `DurationSeconds`, `LibraryId` | ✓ | ✓ | ✓ |
| `Year` | ✓ | ✓ | ✓ (release year) |
| `ReleaseYear` | | ✓ | ✓ |
| `IsWatched`, `Rating` | | ✓ | ✓ |

### Operators

`Equals`, `NotEquals`, `Contains`, `NotContains`, `StartsWith`, `EndsWith`, `GreaterThan`, `LessThan`, `Between`, `InLastDays`, `NotInLastDays`, `IsNull`, `IsNotNull`.

Field-kind metadata in `SmartPlaylistEditorModal.tsx` controls which operators appear for each field. The same restriction is enforced on the backend via the evaluator (unsupported operator → predicate returns `null` → rule is ignored).

### Items endpoint shape

`GET /api/smart-playlists/{id}/items` returns a discriminated payload:

```ts
{ mediaType: 'Music' | 'Movies' | 'Shows',
  tracks?: ArtistTrackVM[],
  movies?: SmartPlaylistMovieVM[],
  episodes?: SmartPlaylistEpisodeVM[] }
```

The frontend detail page renders three different views depending on `mediaType`: a track table (with the music player flow), a poster grid for movies, or an episode list. Clicking a movie/episode navigates to the media detail page (existing playback path); clicking a track queues the whole list via the music player.

## Playlists page UX

`/playlists`:

- Top-level tabs (`All` / `Music` / `Movies & Shows`) persisted in `localStorage` (`playlists_active_tab`).
- A single **+ New** button opens a two-step chooser:
  1. Pick type (Music / Movies / Shows / Mixed).
  2. Pick kind (Manual / Smart). `Mixed` disables Smart (smart playlists need a single type).
- Smart playlists render as tiles with type-colored gradients (fuchsia / sky / amber) and a type badge.
- Manual playlist tiles show the type in the subtitle (`"5 Items · Music"`).

Legacy playlists from before the typed-playlist work default to `Mixed` and only appear under `All`. There's no auto-backfill — users re-categorize by deleting + recreating, or via a (not-yet-built) "Change Type" action on the detail page.

## Adding a new field or operator

1. Add the value to `SmartPlaylistField` or `SmartPlaylistOperator` in `Vora.Application/Media/SmartPlaylists/SmartPlaylistRules.cs`.
2. Wire the field into the relevant accessor in `SmartPlaylistEvaluator.cs` (`MusicFieldAccessor` / `VideoFieldAccessor`).
3. If the operator is new, add a case to `BuildStringRule` / `BuildIntRule` / etc.
4. Add the field to `FIELDS_BY_TYPE` in `SmartPlaylistEditorModal.tsx` so it shows up in the dropdown for the right media type(s).

The TypeScript `SmartPlaylistField` and `SmartPlaylistOperator` string unions in `smartPlaylistService.ts` also need the new value.

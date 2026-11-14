# Vora posters

Vora ships its own collection posters: 75 of them in five groups, drawn by a script in this repo, so every server has them and the project owns them. An admin picks one in a collection's editor next to uploads, links and provider artwork.

| Group | Posters |
|---|---|
| Holidays (20) | New Year's, Lunar New Year, Valentine's Day, Presidents' Day, St. Patrick's Day, Easter, Earth Day, Arbor Day, Mother's Day, Memorial Day, Father's Day, Independence Day, Labor Day, Halloween, Veterans Day, Thanksgiving, Hanukkah, Christmas, Pride Month, Black History Month |
| Popular (8) | Trending, Popular, Top Rated, New Releases, Recently Added, Award Winners, Hidden Gems, Blockbusters |
| Genres (25) | Action, Adventure, Animation, Anime, Biography, Comedy, Crime, Documentary, Drama, Family, Fantasy, History, Horror, Kids, Martial Arts, Music, Mystery, Reality, Romance, Science Fiction, Sports, Superhero, Thriller, War, Western |
| Decades (11) | 1920s to 2020s |
| Ratings (11) | G, PG, PG-13, R, NC-17, TV-Y, TV-Y7, TV-G, TV-PG, TV-14, TV-MA |

## Where they live

- **Files.** `src/Vora.Application/Assets/Posters/<group>/<id>.jpg`, 1000 x 1500 JPEG, about 100 KB each (7.5 MB in all).
- **The list.** `posters.json` beside them names the groups and every poster's `id`, `category`, `title`, `aliases` (the words that find it), `file` and `version` (the first 12 hex digits of the file's SHA-256).
- **Shipping.** `Vora.Application.csproj` copies `Assets\Posters\**` next to the app like the overlay badges, so the Docker image carries them. They are read-only app files, not a storage path: nothing is written there and `StorageRoots` does not list them.

## On the server

- **`IVoraPosterCatalog`** (`Vora.Application/Artwork/VoraPosterCatalog.cs`, a singleton). It reads `posters.json` once, the first time it is asked. A missing or broken list leaves it empty with a warning, never an error. A listed `id` or `category` that is not a safe file name is skipped.
- **`VoraPosterAddress`.** A poster's address is `/api/artwork/vora/<group>/<id>.jpg?v=<version>`. `Find(address)` ignores the version, so an older address still finds the poster.
- **Routes** (`ArtworkEndpoints`):
  - `GET /api/artwork/vora/{category}/{fileName}` serves the file, without signing in (an `<img>` carries no JWT) and cached for 30 days as `immutable`. Anything that is not a listed poster is a 404.
  - `GET /api/artwork/vora` (signed in) returns `VoraPosterCatalogVM`: the groups and every poster with its `url`.
- **Thumbnails.** `ArtworkThumbnailService` resizes `/api/artwork/vora/…` addresses from the shipped file, like `custom/` and `local/`, so tiles load a small copy through `/api/artwork/thumb` (`docs/artwork-image-cache.md`).

## Picking one

- **What is saved.** A pick stores the address in the collection's `PosterUrl`; nothing is copied into the data folder. Backups carry the address and restore it on any server, because every server has the files. Unused files never sees them (`docs/unused-files.md`).
- **Both modals** draw their Posters and Backdrops tabs with `CollectionArtworkPanel` (`components/Collections/`). The Posters tab switches between **Your artwork** (`ArtworkPicker`: upload a file, add from a URL, fetch from an artwork provider, the lock on the selected poster) and **Vora posters** (`VoraPosterPicker`). Backdrops has Your artwork only. A pick locks `PosterUrl` (or `BackdropUrl`), like any picked artwork.
- **Edit modal.** Uploads, links and fetches go straight to the collection (`/api/collections/{id}/artwork/upload`, `/url`, `/fetch`), and Save writes the pick.
- **Create modal.** The same choices, before the collection exists. It opens Posters on the pill holding the current pick.
  - While the admin has not chosen a poster, the one the title or the AI Theme text names is preselected. The General tab says so under the title ("Poster: the Vora Christmas poster. Change"). The suggestion never hides Your artwork, and any other pick replaces it.
  - Clicking the selected poster again leaves the collection without one.
  - **Uploads and links are staged** in the browser (`useStagedArtwork`, `stagedArtwork.ts`) and shown as Manual tiles. Nothing is sent before Create. After `POST /api/collections` returns the id, `saveStagedArtwork` uploads each staged file or link to the new collection (the picked ones first), then sets the picked one with `PUT /api/collections/{id}/artwork/selected?kind=Poster`, which writes only that address (no second sync).
  - **Fetch Artwork** asks `GET /api/collections/artwork/search?providerId=…&title=…`, which saves nothing. A fetched pick goes into the create request like a Vora poster.
  - **If staged artwork fails**, the collection stays, and a dialog names each file or link that was not saved and which of poster and backdrop the collection is left without (`stagedArtworkProblems`).
- **Provider lookup** (`CollectionArtworkService`). A collection with a TMDB id (an automatic one) is looked up by that id. One made by hand, or not created yet, has none, so its title is searched on TMDB (`IMetadataProvider.FindCollectionIdAsync`, TMDB's `search/collection`, first result) and the found id is used for that fetch only, never saved. Providers that search by title (MyAnimeList) get the title as well. Fanart.tv has no collection artwork, so it finds nothing either way.
- **The picker** (`components/Collections/VoraPosterPicker.tsx`):
  - a pill per group;
  - a search over titles and aliases;
  - a grid sized from `--vora-card-min-w-xs`;
  - a **Suggested** label on the suggested poster when another is selected.
- **Loading the list.** `useVoraPosters` loads it through `getVoraPosters` (`api/Media/voraPosterService.ts`), once per server per session.
- **Matching** (`suggestVoraPoster`, `utils/voraPosters.ts`). Text is lowercased, apostrophes are dropped and punctuation becomes spaces, then an alias must appear as whole words. The longest matching alias wins, so "Christmas Horror" gets Christmas. Ratings need "rated" before a one- or two-letter rating ("Rated R"), so "R Kelly" matches nothing. No two posters share an alias.

## When the art changes

1. **Redraw.** Re-rendering a poster gives it a new `version`, so a new address. The old address stays cached in browsers, TV apps and `imagecache/` for good.
2. **Move existing picks.** At startup, after migrations, `VoraPosterAddressRefresher` (`StartupTaskExtensions`) finds every collection whose `PosterUrl` starts with the prefix. It moves those on an older version to the current address and drops the old address's resized copies (`RemoveThumbnailsForSources`).
3. **Withdrawn posters.** A collection pointing at a poster that is no longer listed keeps its address, and the route answers 404.

## Drawing the posters

The posters come from `tools/vora-posters/`. It has no dependencies: Node 22 or later (for the built-in `WebSocket`) drives Edge or Chrome headless through the DevTools protocol.

```
node tools/vora-posters/render.mjs                        # every poster, then posters.json
node tools/vora-posters/render.mjs halloween christmas    # some of them
node tools/vora-posters/render.mjs --sheet sheet.jpg halloween 1980s pg-13   # a contact sheet to review
```

- **The browser.** Set `VORA_POSTER_BROWSER` if Edge or Chrome is not in a usual place. The page loads its fonts from Google Fonts, so drawing needs the internet.
- **`posters.mjs`.** The list: each poster's id, group, title, the small line under it (a date for holidays, the years for decades) and its aliases. `posters.json` is written from it only when every poster has been drawn.
- **`art/*.mjs`.** One file per group. Each poster's look is a palette (`bg`, `a`, `b`, `c`, `ink`), a `draw` function returning SVG for a 1000 x 1000 box, and for decades a period typeface. `art/kit.mjs` holds the shared shapes, gradients and a seeded random so a redraw is identical.
- **`poster.html` / `poster.css` / `poster.mjs`.** The shared layout:
  - the dark ground with glows behind the art;
  - the eyebrow (Holiday, Genre, Decade, Rated, TV rating);
  - the title in Inter Tight 850, fitted to one line when it stays large, else two;
  - the small line under it;
  - the amber Vora V;
  - an inner frame and a little grain.
- **Ownership.** Every shape is drawn by the script, with no third-party images. The decade typefaces (Limelight, Playfair Display, Abril Fatface, Pacifico, Righteous, Shrikhand, Monoton, Bungee, Orbitron) are Google Fonts under the Open Font License, which allows using them in images.

**Adding a poster:** add an entry to `posters.mjs` and a look in its group's `art/` file, render it, review it on a contact sheet, then commit the JPEG with the new `posters.json`. `VoraPosterCatalogTests` fails if:
- a listed file is missing, or a file is not listed;
- a version does not match its file;
- an alias is shared;
- the count is not 75.

## TV apps

A Vora poster is an ordinary relative poster address, so a TV app shows it like an uploaded one. A TV app that sends server images through `/api/artwork/thumb` must treat `/api/artwork/vora/` like `/api/artwork/custom/`, or it loads the full 1000 x 1500 file.

## Tests

- `VoraPosterCatalogTests`: the shipped set, versions, aliases, addresses outside the set and broken lists.
- `VoraPosterAddressRefresherTests`: moving picks to the current version.
- `CollectionPosterAddressTests`: the repository queries.
- `VoraPosterThumbnailTests`: resizing from the shipped file.
- `VoraPosterEndpointTests`: both routes, caching and the 404s.
- `CollectionArtworkLookupTests` / `TmdbCollectionSearchTests`: a collection's provider artwork found by its TMDB id, else by its title. `CollectionManagerArtworkTests`: setting only the poster or backdrop, and the locks a new collection keeps. `CollectionArtworkEndpointTests`: the search and selected routes.
- Web: `voraPosters.test.ts`, `thumbnails.test.ts`, `CreateCollectionModal.test.tsx` (preselect, clicking it off, both pills, a staged upload or link saved after create, a fetched poster, a failed upload keeping the collection) and `EditCollectionModal.test.tsx` (the same panel going straight to the collection).

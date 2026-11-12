# For You

The For You rows on Home and on each library's For You tab. `GET /api/recommendations/global` and `GET /api/libraries/{id}/recommendations` both call `RecommendationManager`, the second narrowed to one library. The clients ask once per provider (`?providerId=`), put the AI provider's rows first, and sort each provider's rows by `weight` **ascending**. So a row's weight is its place on the page, starting at 1, and `RecommendationManager` returns them in that order too. The page is a read of the rows each provider last built (Stored rows, below): no request waits on an AI call. A provider that throws is logged as a warning and the other providers' rows still come. The tiles of every row of library titles are loaded in one query (`GetHydratedMediaItemsAsync` with all their ids); a row at a time made For You fifteen tile queries. A row named by TMDB ids (the AI provider's) is matched on its own, since a tile doesn't carry the id it matched by.

## Stored rows

Each provider's rows are stored per profile, page and provider in `RecommendationSnapshots` (`docs/database.md`): the rows' titles, descriptions, weights and title ids, and when they were built. The tiles are loaded from those ids on every request, through the viewer's access filters and with their watch state as it is now, so a stored row never shows a title the viewer may not see.

- **A request with rows stored** is three statements whatever the provider: whether the provider is on (`is_enabled`), its stored rows (with the profile's last play and last rating, for the local rows), and the tiles. Before, it asked every provider live: the local rows were 65 statements and about 0.9 s on a copy of QA, and the AI rows waited on a chat completion and an embeddings request after every restart, since they were only kept in memory.
- **Out of date** rows are still shown, and a rebuild is queued behind them:
  - a provider that makes outside calls (the AI provider, and any plugin that doesn't say otherwise): rows 12 hours old, as long as the memory cache kept them, so token spend is unchanged;
  - the local provider: the profile played or rated anything since the rows were built (the latest `UserMediaStates.LastPlayedAt` or `UserMediaRatings.RatedAt`), or the rows are 24 hours old, for titles added or gone. Progress reports write `LastPlayedAt`, so watching anything counts.
- **No rows yet:** the local provider's are built while the page waits, once, and stored, so a first visit isn't empty (empty rows are stored too: a profile that has watched nothing reads an empty list until it plays something). Any other provider's are queued, and the page gets none of its rows this time; they show on the next visit.
- **Which kind a provider is:** `IRecommendationProvider.MakesOutsideCalls`, default `true`, so a third-party plugin is never built while a page waits. `LocalRecommendationProvider` says `false`.
- **Rebuilds** run in `ForYouRebuildWorker`, two at a time, each in its own scope (`RecommendationManager.RebuildAsync`), from `IForYouRebuildQueue`:
  - A rebuild already queued or running isn't queued again.
  - One that fails (the AI's monthly token limit, a timeout) is logged as a warning and the rows already stored stay.
  - An outside-call provider that finds nothing isn't kept, so the next look tries again rather than showing no AI rows for twelve hours. Its old rows go too, as they did when the cache's twelve hours ran out: an empty answer is also what a profile whose account turned AI recommendations off gets.
  - Either of those waits 10 minutes (`ForYouRebuildQueue.Cooldown`, in memory) before a page view may queue it again, so a failing or empty provider isn't asked on every view.
  - A rebuild's rows are dated when it started, and a build that started earlier never replaces one that started later.
- **Clearing a profile's viewing history** (watch history or ratings) deletes its stored rows, which were built from it ("Because you watched …"); they are built again from what is left.
- The stored rows aren't backed up: they are built again on the profile's next visit.

## What has been seen

Both providers read the same history (`RecommendationQueries` in Infrastructure):

- **Watched titles** (`WatchedTitlesAsync`): movies and shows the profile played or rated, in the library when one is given.
  - A show counts once, as soon as any of its episodes is played, last watched when its latest episode was. Its own state row is rarely marked played, so going by it alone recommended shows watched to the final episode as unwatched.
  - A title rated but never played here (a Plex import, say) still counts. It has no last-watched time.
- **Never suggested** (`Untouched`): anything the profile played, is part-way through or rated, and any show one of whose episodes was played or started. Both providers apply it, the AI's vector search included.

Ratings are out of 10 (`TasteRatings`): 8 or more is loved, 4 or less disliked.

## Local rows (`LocalRecommendationProvider`)

No AI calls. In page order, interleaved so no kind repeats three times in a row:

1. **Because you watched X**, up to 3: the unstarted titles nearest X in the stored embeddings (`GetSimilarUnwatchedAsync`). Seeds are the most recently watched titles not rated low.
   - The search uses the seed's own vector, against vectors from the same model only. A vector with no model recorded is OpenAI's `text-embedding-3-small`, which is most titles on an older server.
   - A seed with fewer than 4 neighbours is skipped. After 3 such misses (no embeddings yet) these rows are given up.
2. **Because you like {genre}**, up to 4: the best-rated unstarted titles in each of the profile's top genres, in the library. Each watched title adds 1 to each of its genres, 2 if loved and 0 if disliked.
3. **Because you rated X highly**, up to 2: like 1, seeded by the most recently rated loved titles not already used as seeds.
4. **Top rated you haven't seen**: the best-rated unstarted titles in the library.

"Best rated" is `ThirdPartyRating1`, highest first, unrated titles last. Postgres sorts NULL first in a descending order, which used to open the rows with titles that had no score at all.

Each row takes up to 15 titles that no earlier row took, from 40 candidates. A row left with fewer than 4 is dropped. Weights are 1, 2, 3…

## AI rows (`OpenAiRecommendationProvider`)

The model is told:
- up to 12 titles watched most recently, not rated low, with shows named as shows rather than as their last episode;
- up to 8 more rated highly;
- what the page lists: movies, TV shows, or both on Home.

It invents 5 categories. Each one is embedded and searched among the current model's vectors (`VectorSearchUnwatchedMediaAsync`, under `Untouched`).

- **Kept:** stored for 12 hours per profile and library, and only ever built in the background (Stored rows, above). Rows that found nothing aren't kept, and the next look asks again, at most every 10 minutes.
- **Who gets them:** the profile's account needs **Enable AI recommendations** (Users & Access → Requests & AI), an admin's included; it is off for a new account.
- **Requirements:** the AI connection and an embedding model (`docs/ai-connection.md`).

# For You

The For You rows on Home and on each library's For You tab. `GET /api/recommendations/global` and `GET /api/libraries/{id}/recommendations` both call `RecommendationManager`, the second narrowed to one library. The clients ask once per provider (`?providerId=`), put the AI provider's rows first, and sort each provider's rows by `weight` **ascending**. So a row's weight is its place on the page, starting at 1, and `RecommendationManager` returns them in that order too. A provider that throws is logged as a warning and the other providers' rows still come.

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

- **Caching:** rows are kept for 12 hours per profile and library, unless none were found, in which case the next look asks again.
- **Who gets them:** the profile's account needs **Enable AI recommendations** (Users & Access → Requests & AI), an admin's included; it is off for a new account.
- **Requirements:** the AI connection and an embedding model (`docs/ai-connection.md`).

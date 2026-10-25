// Watched state for a detail page, and how many episodes a watched/unwatched
// toggle would actually touch.
//
// The server already derives played state per type — a movie/episode from its
// own UserMediaState, a season from its episodes, a show from every episode
// across its seasons — so `isPlayed` is authoritative for all four types and
// the client must not recompute it.
//
// Recomputing it is what broke shows: the old check required
// `episodes.length > 0`, but MediaDetailsVM only fills `episodes` for a Season.
// A show carries `seasons` instead, so the guard was always false and a fully
// watched show still rendered as unwatched.

export interface WatchStateItem {
    type: string;
    isPlayed?: boolean;
    unplayedItemCount?: number;
    episodes?: { isPlayed?: boolean }[];
    seasons?: { episodeCount?: number }[];
}

export function isFullyWatched(item: WatchStateItem): boolean {
    return item.isPlayed === true;
}

// How many episodes a toggle on this item would update. 0 for a movie or a
// single episode — those are one item and need no warning.
export function affectedEpisodeCount(item: WatchStateItem): number {
    if (item.type === 'TvShow') {
        return (item.seasons ?? []).reduce((total, season) => total + (season.episodeCount ?? 0), 0);
    }
    if (item.type === 'Season') {
        return item.episodes?.length ?? 0;
    }
    return 0;
}

// How many of those episodes the toggle changes: marking a season watched with
// one episode left changes one, not all eight. A season knows each episode's
// state; a show only its count left. Anything else, or a count that doesn't
// fit, falls back to every episode, as Android and the Roku do.
export function changedEpisodeCount(item: WatchStateItem, markWatched: boolean): number {
    const total = affectedEpisodeCount(item);
    const changed = item.type === 'Season'
        ? (item.episodes ?? []).filter(episode => (episode.isPlayed === true) !== markWatched).length
        : item.type === 'TvShow' && item.unplayedItemCount != null
            ? (markWatched ? item.unplayedItemCount : total - item.unplayedItemCount)
            : total;
    return changed < 1 || changed > total ? total : changed;
}

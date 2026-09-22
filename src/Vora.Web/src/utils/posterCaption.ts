// The structured caption shown under a poster/card, consistent everywhere we
// render a list of posters. The shape varies by media type:
//
//   Movie    {Title}            /  {Year} · {Edition}
//   TvShow   {Title}            /  {Year}
//   Season   {Show}             /  Season {name|number}  /  {Year}
//   Episode  {Show}             /  {Episode}              /  S{season} · E{number}
//   Music    {Artist}           /  {Year} · {Album}      /  {Song}
//   Collection {Title}          /  {N items}
//   Playlist {Name}             /  {N items} · {MediaType}
//
// The first entry is the bold primary line; the rest are muted sub-lines.

export interface PosterCaptionItem {
    type?: string;
    title: string;
    tvShowTitle?: string | null;
    seasonNumber?: number | null;
    seasonName?: string | null;
    episodeNumber?: number | null;
    // Set when one file holds several episodes ("E1-E2"), so a card labels a
    // double episode the same way the season list does.
    endEpisodeNumber?: number | null;
    edition?: string | null;
    releaseDate?: string | null;
    // Music
    artistName?: string | null;
    albumTitle?: string | null;
    // Collections and playlists
    itemCount?: number | null;
    mediaTypeLabel?: string | null;
}

export interface PosterCaption {
    title: string;
    lines: string[];
}

function yearOf(releaseDate?: string | null): string | null {
    if (!releaseDate) return null;
    // Read the year straight off the ISO date prefix so a year-only release like
    // "2026-01-01T00:00:00Z" doesn't shift to 2025 in negative timezones the way
    // new Date(...).getFullYear() would.
    const iso = /^(\d{4})-\d{2}-\d{2}/.exec(releaseDate);
    if (iso) return iso[1];
    const d = new Date(releaseDate);
    return Number.isNaN(d.getTime()) ? null : String(d.getFullYear());
}

function dotJoin(parts: (string | null | undefined)[]): string | null {
    const kept = parts.map(p => (p ?? '').toString().trim()).filter(Boolean);
    return kept.length ? kept.join(' · ') : null;
}

// "Season 5", or the season's own name when it's a real title rather than the
// default "Season N" (so a named season like "Specials" shows as-is).
function seasonLabel(item: PosterCaptionItem): string | null {
    const name = item.seasonName?.trim();
    if (name && !/^season\s+\d+$/i.test(name)) {
        return /season|specials/i.test(name) ? name : `Season ${name}`;
    }
    if (item.seasonNumber != null) return `Season ${item.seasonNumber}`;
    return name || null;
}

function itemCountLabel(count?: number | null): string | null {
    if (count == null) return null;
    return `${count} item${count === 1 ? '' : 's'}`;
}

export function posterCaption(item: PosterCaptionItem): PosterCaption {
    const year = yearOf(item.releaseDate);

    switch (item.type) {
        case 'Movie':
            return { title: item.title, lines: [dotJoin([year, item.edition])].filter((l): l is string => !!l) };
        case 'TvShow':
            return { title: item.title, lines: [year].filter((l): l is string => !!l) };
        case 'Season':
            return {
                title: item.tvShowTitle || item.title,
                lines: [seasonLabel(item), year].filter((l): l is string => !!l),
            };
        case 'Episode': {
            // Show name leads, then the episode's own title, then the numbering
            // last — when scanning a rail the title identifies the episode far
            // faster than "S1 · E5" does. Falls back to the named-season label
            // ("Specials · E3") when there's no numeric season.
            const seasonEp = item.seasonNumber != null && item.episodeNumber != null
                ? `S${item.seasonNumber} · E${item.episodeNumber}`
                : dotJoin([seasonLabel(item), item.episodeNumber != null ? `E${item.episodeNumber}` : null]);
            return {
                title: item.tvShowTitle || item.title,
                lines: [item.title, seasonEp].filter((l): l is string => !!l),
            };
        }
        case 'Collection':
            return { title: item.title, lines: [itemCountLabel(item.itemCount)].filter((l): l is string => !!l) };
        case 'Playlist':
            return { title: item.title, lines: [dotJoin([itemCountLabel(item.itemCount), item.mediaTypeLabel])].filter((l): l is string => !!l) };
        case 'Artist':
            return { title: item.title, lines: [] };
        // The album name gets the line to itself. Prefixing it with the year left
        // the part people actually read sharing a narrow card with a number, and
        // it was the first thing to be truncated.
        case 'Album':
            return {
                title: item.artistName || item.title,
                lines: [item.albumTitle || item.title, year].filter((l): l is string => !!l),
            };
        case 'Track':
            return {
                title: item.artistName || item.title,
                lines: [dotJoin([year, item.albumTitle]), item.title].filter((l): l is string => !!l),
            };
        default:
            return { title: item.title, lines: [year].filter((l): l is string => !!l) };
    }
}

// What an episode's corner chip says. Every surface that draws an episode goes
// through this, so a card in a rail and a row in the season list can never
// label the same episode differently.
export interface EpisodeLabelItem {
    type?: string;
    episodeNumber?: number | null;
    endEpisodeNumber?: number | null;
}

export function episodeCornerLabel(item: EpisodeLabelItem | undefined): string | null {
    if (item?.type !== 'Episode' || item.episodeNumber == null) return null;

    // "E1-E2", the form the details page already uses for a double episode.
    return item.endEpisodeNumber != null && item.endEpisodeNumber > item.episodeNumber
        ? `E${item.episodeNumber}-E${item.endEpisodeNumber}`
        : `E${item.episodeNumber}`;
}

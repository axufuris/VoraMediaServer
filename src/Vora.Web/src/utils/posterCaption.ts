// The structured caption shown under a poster/card, consistent everywhere we
// render a list of posters. The shape varies by media type:
//
//   Movie    {Title}            /  {Year} · {Edition}
//   TvShow   {Title}            /  {Year}
//   Season   {Show}             /  Season {name|number}  /  {Year}
//   Episode  {Show}             /  S{season} E{number}    /  {Episode}
//   Music    {Artist}           /  {Year} · {Album}      /  {Song}
//
// The first entry is the bold primary line; the rest are muted sub-lines.

export interface PosterCaptionItem {
    type?: string;
    title: string;
    tvShowTitle?: string | null;
    seasonNumber?: number | null;
    seasonName?: string | null;
    episodeNumber?: number | null;
    edition?: string | null;
    releaseDate?: string | null;
    // Music
    artistName?: string | null;
    albumTitle?: string | null;
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
            // Episode number belongs with the season, not the title: "S1 E5" on
            // one line, the episode name on the next. Falls back to the named-
            // season label ("Specials · E3") when there's no numeric season.
            const seasonEp = item.seasonNumber != null && item.episodeNumber != null
                ? `S${item.seasonNumber} E${item.episodeNumber}`
                : dotJoin([seasonLabel(item), item.episodeNumber != null ? `E${item.episodeNumber}` : null]);
            return {
                title: item.tvShowTitle || item.title,
                lines: [seasonEp, item.title].filter((l): l is string => !!l),
            };
        }
        case 'Artist':
            return { title: item.title, lines: [] };
        case 'Album':
            return { title: item.artistName || item.title, lines: [dotJoin([year, item.albumTitle || item.title])].filter((l): l is string => !!l) };
        case 'Track':
            return {
                title: item.artistName || item.title,
                lines: [dotJoin([year, item.albumTitle]), item.title].filter((l): l is string => !!l),
            };
        default:
            return { title: item.title, lines: [year].filter((l): l is string => !!l) };
    }
}

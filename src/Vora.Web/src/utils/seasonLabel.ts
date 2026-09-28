// Season 0 is where TMDB and TVDB file a show's specials. It reads "Specials"
// everywhere a season/episode is named, never "S0".
export const SPECIALS_SEASON = 0;

export function isSpecialsSeason(seasonNumber?: number | null): boolean {
    return seasonNumber === SPECIALS_SEASON;
}

export function seasonShortLabel(seasonNumber: number): string {
    return isSpecialsSeason(seasonNumber) ? 'Specials' : `S${seasonNumber}`;
}

// "S1 E5", "S1 E5-E6", "Specials E1"; `separator` gives "S1 · E5" for captions.
export function seasonEpisodeLabel(
    seasonNumber: number,
    episodeNumber: number,
    { endEpisodeNumber, separator = ' ' }: { endEpisodeNumber?: number | null; separator?: string } = {},
): string {
    const episode = endEpisodeNumber && endEpisodeNumber > episodeNumber
        ? `E${episodeNumber}-E${endEpisodeNumber}`
        : `E${episodeNumber}`;
    return `${seasonShortLabel(seasonNumber)}${separator}${episode}`;
}

// A season's display name. Season 0 is "Specials" unless it carries a real
// name of its own; any other season keeps its name, or "Season N" without one.
export function seasonName(seasonNumber?: number | null, title?: string | null): string {
    const name = title?.trim();
    if (isSpecialsSeason(seasonNumber) && (!name || /^season\s*0+$/i.test(name))) return 'Specials';
    if (name) return name;
    return seasonNumber != null ? `Season ${seasonNumber}` : '';
}

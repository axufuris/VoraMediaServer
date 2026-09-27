// Which third-party rating platform a provider name refers to, and how its
// score is written. Matched the same way the server-side poster overlay matches
// them (BadgeResolver.ResolveRatingPlatformFile), so a badge on a poster and a
// badge on the detail page always agree.

export type RatingPlatform =
    | 'imdb' | 'rt-critic' | 'rt-audience' | 'tmdb' | 'metacritic'
    | 'trakt' | 'letterboxd' | 'mal' | 'anidb' | 'mdblist';

export const ROTTEN_TOMATOES_FRESH_THRESHOLD = 60;

export function resolveRatingPlatform(name?: string): RatingPlatform | null {
    if (!name) return null;
    const n = name.trim().toLowerCase();

    if (n === 'rotten tomatoes audience' || n === 'rt audience') return 'rt-audience';
    if (n === 'rotten tomatoes' || n === 'rotten tomatoes critic' || n === 'rotten tomatoes certified'
        || n === 'rt' || n === 'rt critic' || n === 'rt certified') return 'rt-critic';

    switch (n) {
        case 'imdb':
        case 'internet movie database':
            return 'imdb';
        case 'tmdb':
        case 'the movie database':
            return 'tmdb';
        case 'metacritic': return 'metacritic';
        case 'trakt': return 'trakt';
        case 'letterboxd': return 'letterboxd';
        case 'mal':
        case 'myanimelist':
            return 'mal';
        case 'anidb': return 'anidb';
        case 'mdblist': return 'mdblist';
        default: return null;
    }
}

export function isPercentScaleRating(name?: string): boolean {
    const platform = resolveRatingPlatform(name);
    return platform === 'rt-critic' || platform === 'rt-audience' || platform === 'metacritic';
}

export function formatRatingValue(value: number, name?: string): string {
    return isPercentScaleRating(name) ? `${Math.round(value)}%` : value.toFixed(1);
}

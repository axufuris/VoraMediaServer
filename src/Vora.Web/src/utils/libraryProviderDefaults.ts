interface ProviderOption {
    id: string;
}

export interface ProviderPreferences {
    metadata: string[];
    rating1: string[];
    rating2: string[];
    artwork: string[];
}

export const PROVIDER_PREFERENCES: Record<number, ProviderPreferences> = {
    1: { metadata: ['tmdb_metadata', 'tvdb_metadata'], rating1: ['omdb_imdb', 'tmdb_rating'], rating2: ['omdb_rotten_tomatoes'], artwork: ['tmdb_artwork', 'fanart_artwork'] },
    2: { metadata: ['tvdb_metadata', 'tmdb_metadata'], rating1: ['omdb_imdb', 'tmdb_rating'], rating2: ['omdb_rotten_tomatoes'], artwork: ['tvdb_artwork', 'tmdb_artwork', 'fanart_artwork'] },
};

const available = (id: string, options: ProviderOption[]) => options.some(o => o.id === id);

export function keepOrPick(current: string, options: ProviderOption[], preferred: string[], exclude = ''): string {
    if (!current || (available(current, options) && current !== exclude)) return current;
    return preferred.find(id => id !== exclude && available(id, options)) ?? '';
}

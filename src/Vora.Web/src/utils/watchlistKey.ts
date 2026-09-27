// A watchlist entry is a (provider, external id) pair: TMDB 550 and TVDB 550 are
// different titles, so keying on the external id alone marks the wrong rows.
export const watchlistKey = (providerId: string, externalId: string): string => `${providerId}:${externalId}`;

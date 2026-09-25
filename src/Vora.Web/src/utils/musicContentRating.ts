// An album is marked Explicit when any track on it is, as music stores do.
export function hasExplicitTrack(tracks: { contentRating?: string | null }[]): boolean {
    return tracks.some(t => t.contentRating === 'Explicit');
}

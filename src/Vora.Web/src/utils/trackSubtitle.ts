// "Artist — Album", leaving out whichever part is missing. Building it inline
// with a template literal left a dangling separator: the artist-track lists
// passed an empty artist and the now-playing screen showed "— Cheshire Cat".
export function trackSubtitle(artist?: string | null, album?: string | null): string {
    return [artist, album]
        .map(part => part?.trim())
        .filter((part): part is string => !!part)
        .join(' — ');
}

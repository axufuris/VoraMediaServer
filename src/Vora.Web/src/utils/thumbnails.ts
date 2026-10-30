// Routes images through the server-side image cache so grid cells load small,
// resized JPEGs instead of full-resolution artwork. Only artwork the server
// holds (uploads and overlays under custom/, images read from a title's media
// folder under local/, the posters that ship with Vora under vora/) and remote
// http(s) provider images are cached; anything
// else (data URIs, unknown schemes) is returned unchanged. `kind` selects the cache bucket
// (posters vs stills vs backdrops vs logos) so different image classes stay
// separate — and so a logo is cached as a PNG with its transparency intact
// rather than flattened onto black by the JPEG encoder.
type ThumbKind = 'poster' | 'still' | 'backdrop' | 'logo';

export function thumbUrl(src: string | undefined | null, width: number, kind: ThumbKind = 'poster'): string | undefined {
    if (!src) return src ?? undefined;

    const isRemote = /^https?:\/\//i.test(src);
    const isOnTheServer = src.startsWith('/api/artwork/custom/') || src.startsWith('/api/artwork/local/') || src.startsWith('/api/artwork/vora/');
    if (!isRemote && !isOnTheServer) return src;

    return `/api/artwork/thumb?w=${width}&kind=${kind}&src=${encodeURIComponent(src)}`;
}

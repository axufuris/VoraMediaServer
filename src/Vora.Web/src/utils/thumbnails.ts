// Routes images through the server-side image cache so grid cells load small,
// resized JPEGs instead of full-resolution artwork. Only local custom artwork
// and remote http(s) provider images are cached; anything else (data URIs,
// unknown schemes) is returned unchanged. `kind` selects the cache bucket
// (posters vs stills vs backdrops) so different image classes stay separate.
type ThumbKind = 'poster' | 'still' | 'backdrop';

export function thumbUrl(src: string | undefined | null, width: number, kind: ThumbKind = 'poster'): string | undefined {
    if (!src) return src ?? undefined;

    const isRemote = /^https?:\/\//i.test(src);
    const isCustom = src.startsWith('/api/artwork/custom/');
    if (!isRemote && !isCustom) return src;

    return `/api/artwork/thumb?w=${width}&kind=${kind}&src=${encodeURIComponent(src)}`;
}

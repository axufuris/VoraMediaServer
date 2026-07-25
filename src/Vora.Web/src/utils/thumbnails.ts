// Routes poster/still images through the server-side thumbnail cache so grid
// cells load small, resized JPEGs instead of full-resolution posters. Only
// local custom artwork and remote http(s) provider images are cached; anything
// else (data URIs, unknown schemes) is returned unchanged.
export function thumbUrl(src: string | undefined | null, width: number): string | undefined {
    if (!src) return src ?? undefined;

    const isRemote = /^https?:\/\//i.test(src);
    const isCustom = src.startsWith('/api/artwork/custom/');
    if (!isRemote && !isCustom) return src;

    return `/api/artwork/thumb?w=${width}&src=${encodeURIComponent(src)}`;
}

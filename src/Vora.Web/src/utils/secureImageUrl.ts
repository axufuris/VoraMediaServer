import { thumbUrl } from './thumbnails';

const INSECURE_PREFIX = /^http:\/\//i;
const WIDTH = 500;

export type RemoteImageKind = 'logo' | 'poster';

// A browser will not load an http:// image into an https:// page: it retries the
// address over https, and a host without https shows nothing. So on an https page
// such an image is asked for through the server, which fetches and caches it. The
// server only does that for an address it sent out itself. On an http page, and
// for every other address, the image loads directly as before.
export function secureImageUrl(
    url: string | null | undefined,
    kind: RemoteImageKind = 'logo',
    pageProtocol: string = window.location.protocol,
): string | undefined {
    if (!url) return undefined;
    if (pageProtocol !== 'https:' || !INSECURE_PREFIX.test(url)) return url;
    return thumbUrl(url, WIDTH, kind);
}

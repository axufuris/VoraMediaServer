const INSECURE_PREFIX = /^http:\/\//i;

export function secureImageUrl(url: string | null | undefined, pageProtocol: string = window.location.protocol): string | undefined {
    if (!url) return undefined;
    if (pageProtocol !== 'https:' || !INSECURE_PREFIX.test(url)) return url;
    return `https://${url.replace(INSECURE_PREFIX, '')}`;
}

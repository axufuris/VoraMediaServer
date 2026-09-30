const ALLOWED_PROTOCOLS = new Set(['http:', 'https:', 'blob:']);

export function safeMediaUrl(raw: string | null | undefined): string | undefined {
    const trimmed = raw?.trim();
    if (!trimmed) return undefined;
    try {
        const url = new URL(trimmed, window.location.origin);
        return ALLOWED_PROTOCOLS.has(url.protocol) ? url.href : undefined;
    } catch {
        return undefined;
    }
}

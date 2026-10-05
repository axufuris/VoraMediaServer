export function normalizePublicUrl(value: string): string | null {
    const trimmed = value.trim().replace(/\/+$/, '');
    if (!trimmed) return null;
    const withScheme = /^[a-z][a-z0-9+.-]*:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`;
    try {
        const url = new URL(withScheme);
        const webScheme = url.protocol === 'http:' || url.protocol === 'https:';
        return webScheme && url.hostname.includes('.') ? withScheme : null;
    } catch {
        return null;
    }
}

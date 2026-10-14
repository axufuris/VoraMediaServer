// "jpn" and "ja" read as "Japanese"; a missing or unreadable code is shown as it is.
export function languageName(code?: string): string {
    if (!code) return 'Unknown';
    try {
        return new Intl.DisplayNames(['en'], { type: 'language' }).of(new Intl.Locale(code).language) ?? code;
    } catch {
        return code;
    }
}

export function audioLayout(channels?: number): string {
    if (channels === 8) return '7.1';
    if (channels === 6) return '5.1';
    if (channels === 2) return '2.0';
    if (channels === 1) return '1.0';
    return channels ? `${channels}ch` : '';
}

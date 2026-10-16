// "jpn" and "ja" read as "Japanese". The scanner stores English as the name
// "English" rather than a code, and Intl would lower-case a name it cannot
// read as a code, so a spelled-out name keeps its own spelling. A missing or
// unreadable code is shown as it is.
export function languageName(code?: string): string {
    const trimmed = code?.trim();
    if (!trimmed) return 'Unknown';
    if (/^[a-z]{4,}$/i.test(trimmed)) return trimmed[0].toUpperCase() + trimmed.slice(1).toLowerCase();
    try {
        return new Intl.DisplayNames(['en'], { type: 'language' }).of(new Intl.Locale(trimmed).language) ?? trimmed;
    } catch {
        return trimmed;
    }
}

// The language a track is in, or null when the file does not say ("und" is
// how a muxer writes "not set").
export function knownLanguageName(code?: string): string | null {
    const trimmed = code?.trim();
    if (!trimmed || trimmed.toLowerCase() === 'und') return null;
    return languageName(trimmed);
}

export function audioLayout(channels?: number): string {
    if (channels === 8) return '7.1';
    if (channels === 6) return '5.1';
    if (channels === 2) return '2.0';
    if (channels === 1) return '1.0';
    return channels ? `${channels}ch` : '';
}

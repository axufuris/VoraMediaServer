import type { VoraPoster } from '../api/Media/voraPosterService';

export const isVoraPosterUrl = (url: string | undefined | null): boolean => !!url && url.startsWith('/api/artwork/vora/');

const words = (text: string) => ` ${text.toLowerCase().replace(/['’]/g, '').replace(/[^a-z0-9]+/g, ' ').trim()} `;

export function suggestVoraPoster(posters: VoraPoster[], texts: (string | undefined)[]): VoraPoster | undefined {
    const written = texts.filter((text): text is string => !!text && text.trim().length > 0).map(words);
    if (written.length === 0) return undefined;

    let best: { poster: VoraPoster; length: number } | undefined;
    for (const poster of posters) {
        for (const alias of poster.aliases) {
            const wanted = words(alias);
            if (wanted.trim().length === 0) continue;
            if (written.some(text => text.includes(wanted)) && (!best || wanted.length > best.length)) {
                best = { poster, length: wanted.length };
            }
        }
    }
    return best?.poster;
}

export function matchesVoraPosterSearch(poster: VoraPoster, search: string): boolean {
    const wanted = words(search).trim();
    if (wanted.length === 0) return true;
    return [poster.title, ...poster.aliases].some(text => words(text).includes(wanted));
}

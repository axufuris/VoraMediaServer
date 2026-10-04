import { StorageKeys } from './storageKeys';

export type SynthStyle = 'bars' | 'waves' | 'ring';
export type SynthColors = 'art' | 'theme';

export const SYNTH_STYLES: readonly SynthStyle[] = ['bars', 'waves', 'ring'];
export const SYNTH_COLORS: readonly SynthColors[] = ['art', 'theme'];

const readText = (key: string): string | null => {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
};

const writeText = (key: string, value: string): void => {
    try {
        localStorage.setItem(key, value);
    } catch {
        /* storage unavailable — the choice lasts until the page closes */
    }
};

const read = (key: string): boolean => readText(key) === 'true';
const write = (key: string, on: boolean): void => writeText(key, on ? 'true' : 'false');

const readChoice = <T extends string>(key: string, allowed: readonly T[], fallback: T): T => {
    const saved = readText(key);
    return allowed.find(value => value === saved) ?? fallback;
};

export const nowPlayingViewStore = {
    lyrics: (): boolean => read(StorageKeys.nowPlayingLyrics),
    setLyrics: (on: boolean): void => write(StorageKeys.nowPlayingLyrics, on),
    synth: (): boolean => read(StorageKeys.nowPlayingSynth),
    setSynth: (on: boolean): void => write(StorageKeys.nowPlayingSynth, on),
    synthStyle: (): SynthStyle => readChoice(StorageKeys.nowPlayingSynthStyle, SYNTH_STYLES, 'bars'),
    setSynthStyle: (style: SynthStyle): void => writeText(StorageKeys.nowPlayingSynthStyle, style),
    synthColors: (): SynthColors => readChoice(StorageKeys.nowPlayingSynthColors, SYNTH_COLORS, 'art'),
    setSynthColors: (colors: SynthColors): void => writeText(StorageKeys.nowPlayingSynthColors, colors),
};

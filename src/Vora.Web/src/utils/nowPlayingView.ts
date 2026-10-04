import { StorageKeys } from './storageKeys';

const read = (key: string): boolean => {
    try {
        return localStorage.getItem(key) === 'true';
    } catch {
        return false;
    }
};

const write = (key: string, on: boolean): void => {
    try {
        localStorage.setItem(key, on ? 'true' : 'false');
    } catch {
        /* storage unavailable — the choice lasts until the page closes */
    }
};

export const nowPlayingViewStore = {
    lyrics: (): boolean => read(StorageKeys.nowPlayingLyrics),
    setLyrics: (on: boolean): void => write(StorageKeys.nowPlayingLyrics, on),
    synth: (): boolean => read(StorageKeys.nowPlayingSynth),
    setSynth: (on: boolean): void => write(StorageKeys.nowPlayingSynth, on),
};

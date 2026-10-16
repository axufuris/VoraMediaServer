import { StorageKeys } from './storageKeys';
import { getCurrentProfileDeviceIds } from './hardwareScanner';

export interface PlayerKeySteps {
    seekSeconds: number;
    volumePercent: number;
}

export type PlayerKeyAction =
    | { kind: 'seek'; seconds: number }
    | { kind: 'volume'; percent: number };

export const SEEK_STEP_CHOICES: readonly number[] = [5, 10, 15, 30];
export const VOLUME_STEP_CHOICES: readonly number[] = [2, 5, 10];
export const DEFAULT_PLAYER_KEY_STEPS: PlayerKeySteps = { seekSeconds: 10, volumePercent: 5 };

interface KeyLike {
    key: string;
    altKey: boolean;
    ctrlKey: boolean;
    metaKey: boolean;
    shiftKey: boolean;
}

// Left/Right seek and Up/Down change the volume. A key held with a modifier
// belongs to the browser or the system (Alt+Left is Back), so it is left alone.
export function playerKeyAction(event: KeyLike, steps: PlayerKeySteps, canSeek: boolean): PlayerKeyAction | null {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return null;
    switch (event.key) {
        case 'ArrowRight': return canSeek ? { kind: 'seek', seconds: steps.seekSeconds } : null;
        case 'ArrowLeft': return canSeek ? { kind: 'seek', seconds: -steps.seekSeconds } : null;
        case 'ArrowUp': return { kind: 'volume', percent: steps.volumePercent };
        case 'ArrowDown': return { kind: 'volume', percent: -steps.volumePercent };
        default: return null;
    }
}

const NonTypingInputTypes = new Set(['range', 'checkbox', 'radio', 'button', 'submit', 'reset', 'color', 'file', 'image']);

// Arrow keys move the caret in a text field and the choice in a list, so the
// player keeps its hands off while one of those has focus. A slider is not
// typing: the volume slider would otherwise take Up/Down for itself and Left
// and Right too, so a focused one gets the player's keys like everything else.
export function isTypingTarget(target: EventTarget | null): boolean {
    if (!(target instanceof HTMLElement)) return false;
    if (target.closest('[contenteditable]:not([contenteditable="false"])')) return true;
    if (target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement) return true;
    if (target instanceof HTMLInputElement) return !NonTypingInputTypes.has(target.type);
    return false;
}

export function nextVolume(volume: number, percent: number): number {
    const next = Math.round((volume * 100) + percent) / 100;
    return Math.min(1, Math.max(0, next));
}

interface SavedKeySteps {
    seekStepSeconds?: number;
    volumeStepPercent?: number;
}

const choiceOrDefault = (value: number | undefined, choices: readonly number[], fallback: number): number =>
    typeof value === 'number' && choices.includes(value) ? value : fallback;

// The steps live in the profile's playback settings for this device, beside
// its bitrate and resolution caps, and travel with them to the server. Those
// settings can hold an old bare number instead of an object, which reads as
// nothing saved.
export function readPlayerKeySteps(profileId: string, deviceId: string): PlayerKeySteps {
    try {
        const saved = localStorage.getItem(StorageKeys.playbackPrefs(profileId, deviceId));
        const prefs: SavedKeySteps | null = saved ? JSON.parse(saved) : null;
        return {
            seekSeconds: choiceOrDefault(prefs?.seekStepSeconds, SEEK_STEP_CHOICES, DEFAULT_PLAYER_KEY_STEPS.seekSeconds),
            volumePercent: choiceOrDefault(prefs?.volumeStepPercent, VOLUME_STEP_CHOICES, DEFAULT_PLAYER_KEY_STEPS.volumePercent),
        };
    } catch {
        return DEFAULT_PLAYER_KEY_STEPS;
    }
}

export function currentPlayerKeySteps(): PlayerKeySteps {
    const { profileId, deviceId } = getCurrentProfileDeviceIds();
    return readPlayerKeySteps(profileId, deviceId);
}

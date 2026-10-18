import { StorageKeys } from './storageKeys';
import { getCurrentProfileDeviceIds } from './hardwareScanner';

export interface PlayerKeySteps {
    seekSeconds: number;
    volumePercent: number;
}

export type PlayerKeyAction =
    | { kind: 'seek'; seconds: number }
    | { kind: 'volume'; percent: number }
    | { kind: 'playPause' };

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

// Space plays and pauses. Left/Right seek and Up/Down change the volume, with
// or without Shift. A screen whose plain arrows move focus between its buttons
// for a TV remote's D-pad (the audio now-playing screens) passes plainArrows
// false and answers only Shift with an arrow. Alt, Ctrl and Meta belong to the
// browser or the system (Alt+Left is Back), and Shift+Space scrolls up, so
// those are left alone.
export function playerKeyAction(event: KeyLike, steps: PlayerKeySteps, canSeek: boolean, plainArrows = true): PlayerKeyAction | null {
    if (event.altKey || event.ctrlKey || event.metaKey) return null;
    if (event.key === ' ' || event.key === 'Spacebar') return event.shiftKey ? null : { kind: 'playPause' };
    if (!event.shiftKey && !plainArrows) return null;
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

const SpaceActivatedSelector = [
    'button',
    'a[href]',
    'summary',
    'select',
    'textarea',
    '[contenteditable]:not([contenteditable="false"])',
    '[role="button"]',
    '[role="link"]',
    '[role="checkbox"]',
    '[role="switch"]',
    '[role="radio"]',
    '[role="tab"]',
    '[role="menuitem"]',
    '[role="menuitemcheckbox"]',
    '[role="menuitemradio"]',
    '[role="option"]',
    '[role="treeitem"]',
].join(',');

// Space presses the focused button, ticks the focused box and types a space in
// a field, and those keep it: after a click on the play button, Space presses
// it again rather than toggling twice. Anywhere else (the picture, a slider,
// the page) it belongs to the player.
export function spaceBelongsToTarget(target: EventTarget | null): boolean {
    if (!(target instanceof HTMLElement)) return false;
    if (target instanceof HTMLInputElement) return target.type !== 'range';
    return target.closest(SpaceActivatedSelector) !== null;
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

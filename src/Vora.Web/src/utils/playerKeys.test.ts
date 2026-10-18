import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_PLAYER_KEY_STEPS, isTypingTarget, nextVolume, playerKeyAction, readPlayerKeySteps, spaceBelongsToTarget } from './playerKeys';

const key = (name: string, modifiers: Partial<{ altKey: boolean; ctrlKey: boolean; metaKey: boolean; shiftKey: boolean }> = {}) =>
    ({ key: name, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, ...modifiers });

describe('playerKeyAction', () => {
    const steps = { seekSeconds: 10, volumePercent: 5 };

    it.each([
        ['ArrowRight', { kind: 'seek', seconds: 10 }],
        ['ArrowLeft', { kind: 'seek', seconds: -10 }],
        ['ArrowUp', { kind: 'volume', percent: 5 }],
        ['ArrowDown', { kind: 'volume', percent: -5 }],
    ])('maps %s', (name, action) => {
        expect(playerKeyAction(key(name), steps, true)).toEqual(action);
    });

    it('uses the saved steps', () => {
        expect(playerKeyAction(key('ArrowRight'), { seekSeconds: 30, volumePercent: 10 }, true)).toEqual({ kind: 'seek', seconds: 30 });
        expect(playerKeyAction(key('ArrowDown'), { seekSeconds: 30, volumePercent: 10 }, true)).toEqual({ kind: 'volume', percent: -10 });
    });

    it('leaves Left and Right alone where the stream cannot seek, but still changes the volume', () => {
        expect(playerKeyAction(key('ArrowLeft'), steps, false)).toBeNull();
        expect(playerKeyAction(key('ArrowRight'), steps, false)).toBeNull();
        expect(playerKeyAction(key('ArrowUp'), steps, false)).toEqual({ kind: 'volume', percent: 5 });
    });

    it('leaves a key held with Alt, Ctrl or Meta to the browser', () => {
        expect(playerKeyAction(key('ArrowLeft', { altKey: true }), steps, true)).toBeNull();
        expect(playerKeyAction(key('ArrowRight', { ctrlKey: true }), steps, true)).toBeNull();
        expect(playerKeyAction(key('ArrowUp', { metaKey: true }), steps, true)).toBeNull();
        expect(playerKeyAction(key(' ', { ctrlKey: true }), steps, true)).toBeNull();
    });

    it('answers Shift with an arrow the same as the plain arrow', () => {
        expect(playerKeyAction(key('ArrowRight', { shiftKey: true }), steps, true)).toEqual({ kind: 'seek', seconds: 10 });
        expect(playerKeyAction(key('ArrowDown', { shiftKey: true }), steps, true)).toEqual({ kind: 'volume', percent: -5 });
    });

    it('answers only Shift with an arrow where the plain arrows move focus', () => {
        expect(playerKeyAction(key('ArrowRight'), steps, true, false)).toBeNull();
        expect(playerKeyAction(key('ArrowUp'), steps, true, false)).toBeNull();
        expect(playerKeyAction(key('ArrowLeft', { shiftKey: true }), steps, true, false)).toEqual({ kind: 'seek', seconds: -10 });
        expect(playerKeyAction(key('ArrowUp', { shiftKey: true }), steps, true, false)).toEqual({ kind: 'volume', percent: 5 });
    });

    it('plays and pauses on Space, and leaves Shift+Space to scroll the page', () => {
        expect(playerKeyAction(key(' '), steps, true)).toEqual({ kind: 'playPause' });
        expect(playerKeyAction(key(' '), steps, false, false)).toEqual({ kind: 'playPause' });
        expect(playerKeyAction(key('Spacebar'), steps, true)).toEqual({ kind: 'playPause' });
        expect(playerKeyAction(key(' ', { shiftKey: true }), steps, true)).toBeNull();
    });

    it('ignores every other key', () => {
        expect(playerKeyAction(key('k'), steps, true)).toBeNull();
        expect(playerKeyAction(key('Enter'), steps, true)).toBeNull();
    });
});

describe('spaceBelongsToTarget', () => {
    const element = <K extends keyof HTMLElementTagNameMap>(tag: K, setup?: (el: HTMLElementTagNameMap[K]) => void) => {
        const el = document.createElement(tag);
        setup?.(el);
        return el;
    };

    it('leaves Space to a focused button, link, box or field, down to the icon inside a button', () => {
        const button = element('button');
        const icon = element('span');
        button.appendChild(icon);
        document.body.appendChild(button);

        expect(spaceBelongsToTarget(button)).toBe(true);
        expect(spaceBelongsToTarget(icon)).toBe(true);
        expect(spaceBelongsToTarget(element('a', el => { el.href = '/library/1'; }))).toBe(true);
        expect(spaceBelongsToTarget(element('input', el => { el.type = 'checkbox'; }))).toBe(true);
        expect(spaceBelongsToTarget(element('input'))).toBe(true);
        expect(spaceBelongsToTarget(element('div', el => el.setAttribute('role', 'menuitem')))).toBe(true);
        button.remove();
    });

    it('gives Space to the player on a slider, the picture or the page', () => {
        expect(spaceBelongsToTarget(element('input', el => { el.type = 'range'; }))).toBe(false);
        expect(spaceBelongsToTarget(element('div', el => el.setAttribute('role', 'slider')))).toBe(false);
        expect(spaceBelongsToTarget(element('video'))).toBe(false);
        expect(spaceBelongsToTarget(document.body)).toBe(false);
        expect(spaceBelongsToTarget(null)).toBe(false);
    });
});

describe('isTypingTarget', () => {
    const element = <K extends keyof HTMLElementTagNameMap>(tag: K, setup?: (el: HTMLElementTagNameMap[K]) => void) => {
        const el = document.createElement(tag);
        setup?.(el);
        return el;
    };

    it('treats text fields, text areas and lists as typing', () => {
        expect(isTypingTarget(element('input'))).toBe(true);
        expect(isTypingTarget(element('input', el => { el.type = 'search'; }))).toBe(true);
        expect(isTypingTarget(element('input', el => { el.type = 'number'; }))).toBe(true);
        expect(isTypingTarget(element('textarea'))).toBe(true);
        expect(isTypingTarget(element('select'))).toBe(true);
    });

    it('treats an editable region as typing, down to the element inside it', () => {
        const editor = element('div', el => el.setAttribute('contenteditable', 'true'));
        const inner = element('span');
        editor.appendChild(inner);
        document.body.appendChild(editor);

        expect(isTypingTarget(editor)).toBe(true);
        expect(isTypingTarget(inner)).toBe(true);
        editor.remove();
    });

    it('does not treat the volume slider, buttons or the page as typing', () => {
        expect(isTypingTarget(element('input', el => { el.type = 'range'; }))).toBe(false);
        expect(isTypingTarget(element('input', el => { el.type = 'checkbox'; }))).toBe(false);
        expect(isTypingTarget(element('button'))).toBe(false);
        expect(isTypingTarget(element('div'))).toBe(false);
        expect(isTypingTarget(element('div', el => el.setAttribute('contenteditable', 'false')))).toBe(false);
        expect(isTypingTarget(window)).toBe(false);
        expect(isTypingTarget(null)).toBe(false);
    });
});

describe('nextVolume', () => {
    it('steps in whole percents without drifting', () => {
        expect(nextVolume(0.15, 5)).toBe(0.2);
        expect(nextVolume(0.7, -5)).toBe(0.65);
    });

    it('stops at silent and at full', () => {
        expect(nextVolume(0.98, 5)).toBe(1);
        expect(nextVolume(1, 5)).toBe(1);
        expect(nextVolume(0.03, -5)).toBe(0);
        expect(nextVolume(0, -5)).toBe(0);
    });
});

describe('readPlayerKeySteps', () => {
    beforeEach(() => localStorage.clear());

    it('defaults to 10 seconds and 5% with nothing saved', () => {
        expect(readPlayerKeySteps('profile', 'device')).toEqual({ seekSeconds: 10, volumePercent: 5 });
        expect(DEFAULT_PLAYER_KEY_STEPS).toEqual({ seekSeconds: 10, volumePercent: 5 });
    });

    it('reads the steps saved with the playback settings for this profile and device', () => {
        localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ bitrate: 20, seekStepSeconds: 30, volumeStepPercent: 10 }));

        expect(readPlayerKeySteps('profile', 'device')).toEqual({ seekSeconds: 30, volumePercent: 10 });
        expect(readPlayerKeySteps('someone-else', 'device')).toEqual({ seekSeconds: 10, volumePercent: 5 });
    });

    it('falls back for a value the settings never offer', () => {
        localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ seekStepSeconds: 7, volumeStepPercent: '5' }));

        expect(readPlayerKeySteps('profile', 'device')).toEqual({ seekSeconds: 10, volumePercent: 5 });
    });

    it('treats the bare number older settings hold, or unreadable JSON, as nothing saved', () => {
        localStorage.setItem('playback_prefs_profile_device', '0');
        expect(readPlayerKeySteps('profile', 'device')).toEqual({ seekSeconds: 10, volumePercent: 5 });

        localStorage.setItem('playback_prefs_profile_device', '{not json');
        expect(readPlayerKeySteps('profile', 'device')).toEqual({ seekSeconds: 10, volumePercent: 5 });
    });
});

import { describe, it, expect, beforeEach } from 'vitest';
import { nowPlayingViewStore } from './nowPlayingView';

describe('nowPlayingViewStore synth choices', () => {
    beforeEach(() => localStorage.clear());

    it('starts on bars coloured from the album art', () => {
        expect(nowPlayingViewStore.synthStyle()).toBe('bars');
        expect(nowPlayingViewStore.synthColors()).toBe('art');
    });

    it('remembers the style and colours chosen', () => {
        nowPlayingViewStore.setSynthStyle('ring');
        nowPlayingViewStore.setSynthColors('theme');

        expect(nowPlayingViewStore.synthStyle()).toBe('ring');
        expect(nowPlayingViewStore.synthColors()).toBe('theme');
        expect(localStorage.getItem('now_playing_synth_style')).toBe('ring');
        expect(localStorage.getItem('now_playing_synth_colors')).toBe('theme');
    });

    it('ignores a saved value it does not know', () => {
        localStorage.setItem('now_playing_synth_style', 'lasers');

        expect(nowPlayingViewStore.synthStyle()).toBe('bars');
    });
});

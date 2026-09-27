import { describe, it, expect } from 'vitest';
import { usesNowPlayingScreen } from './nowPlayingScreen';

describe('which media gets the now-playing screen', () => {
    it('sends music to the now-playing screen', () => {
        expect(usesNowPlayingScreen({ playbackContextType: 'Music' })).toBe(true);
    });

    // These still expand into their own layouts; routing them to the music
    // screen would show a queue and lyrics for a live station.
    it.each(['LiveRadio', 'Podcast', 'Movie', 'Episode', 'LiveTv'])('keeps %s on its own player', type => {
        expect(usesNowPlayingScreen({ playbackContextType: type })).toBe(false);
    });

    it.each([null, undefined, {}])('treats %s as not music', media => {
        expect(usesNowPlayingScreen(media)).toBe(false);
    });

    it('is exact about the type name', () => {
        expect(usesNowPlayingScreen({ playbackContextType: 'music' })).toBe(false);
    });
});

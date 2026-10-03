import { describe, expect, it } from 'vitest';
import { moodName, songFeelParts } from './songFeel';

describe('songFeelParts', () => {
    it('capitalises up to three moods and adds energy and instrumental', () => {
        expect(songFeelParts({ moods: ['euphoric', 'upbeat', 'warm', 'playful'], energy: 'High', isInstrumental: true }))
            .toEqual(['Euphoric', 'Upbeat', 'Warm', 'High energy', 'Instrumental']);
    });

    it('is empty for a song not described yet', () => {
        expect(songFeelParts({})).toEqual([]);
    });
});

describe('moodName', () => {
    it('capitalises a mood for display and is empty without one', () => {
        expect(moodName('melancholy')).toBe('Melancholy');
        expect(moodName(undefined)).toBe('');
    });
});

import { describe, expect, it } from 'vitest';
import { mediaPath } from './mediaPath';

describe('mediaPath', () => {
    it('opens an episode on its season, with the episode selected', () => {
        expect(mediaPath({ id: 'ep-2', type: 'Episode', seasonId: 'season-1' })).toBe('/media/season-1?episode=ep-2');
        expect(mediaPath({ id: 'ep-2', type: 'Episode', seasonId: 'season-1' }, 'home')).toBe('/server/home/media/season-1?episode=ep-2');
    });

    it('opens an episode whose season is not known on its own address, which then moves to the season', () => {
        expect(mediaPath({ id: 'ep-2', type: 'Episode' })).toBe('/media/ep-2');
    });

    it('opens every other title on its own page', () => {
        expect(mediaPath({ id: 'shogun', type: 'TvShow' })).toBe('/media/shogun');
        expect(mediaPath({ id: 'season-1', type: 'Season' }, 'home')).toBe('/server/home/media/season-1');
        expect(mediaPath({ id: 'film' })).toBe('/media/film');
    });
});

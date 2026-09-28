import { describe, it, expect } from 'vitest';
import { seasonEpisodeLabel, seasonName } from './seasonLabel';
import { posterCaption } from './posterCaption';

describe('seasonEpisodeLabel', () => {
    it('calls season 0 Specials, never S0', () => {
        expect(seasonEpisodeLabel(0, 1)).toBe('Specials E1');
        expect(seasonEpisodeLabel(0, 1, { separator: ' · ' })).toBe('Specials · E1');
    });

    it('keeps S1 E5 for a regular season, with a range when there is one', () => {
        expect(seasonEpisodeLabel(1, 5)).toBe('S1 E5');
        expect(seasonEpisodeLabel(1, 5, { endEpisodeNumber: 6 })).toBe('S1 E5-E6');
        expect(seasonEpisodeLabel(1, 5, { endEpisodeNumber: 5 })).toBe('S1 E5');
    });

    it('labels a special under a poster as Specials', () => {
        const caption = posterCaption({ type: 'Episode', title: 'Hard Laughs Featurette', tvShowTitle: 'Titus', seasonNumber: 0, episodeNumber: 1 });

        expect(caption.lines).toEqual(['Hard Laughs Featurette', 'Specials · E1']);
    });
});

describe('seasonName', () => {
    it('names season 0 Specials when it only has a numbered name or none', () => {
        expect(seasonName(0, 'Season 0')).toBe('Specials');
        expect(seasonName(0, null)).toBe('Specials');
        expect(seasonName(0, 'Behind the Scenes')).toBe('Behind the Scenes');
    });

    it("keeps any other season's name, or numbers it", () => {
        expect(seasonName(1, 'Season 1')).toBe('Season 1');
        expect(seasonName(3, null)).toBe('Season 3');
    });

    it('captions a season 0 tile as Specials', () => {
        const caption = posterCaption({ type: 'Season', title: 'Season 0', tvShowTitle: 'Titus', seasonNumber: 0, seasonName: 'Season 0' });

        expect(caption.lines[0]).toBe('Specials');
    });
});

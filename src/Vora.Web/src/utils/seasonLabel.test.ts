import { describe, it, expect } from 'vitest';
import { seasonEpisodeLabel } from './seasonLabel';
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

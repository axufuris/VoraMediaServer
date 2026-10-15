import { describe, expect, it } from 'vitest';
import { migrationSummary, sharedTitleNote } from './migrationSummary';

const counts = {
    watchStatesFetched: 13360, watchStatesImported: 13273, watchStatesMerged: 87,
    ratingsFetched: 756, ratingsImported: 753, ratingsMerged: 3,
};

describe('the import summary', () => {
    it('counts entries that shared a title apart from ones that are missing', () => {
        expect(migrationSummary(counts)).toBe(
            'Watch states: 13,273 titles from 13,360 Plex entries, 87 shared a title, none missing · Ratings: 753 titles from 756 Plex entries, 3 shared a title, none missing');
        expect(sharedTitleNote(counts)).toMatch(/multi-episode file/);
    });

    it('says how many entries Vora has no title for', () => {
        expect(migrationSummary({ ...counts, watchStatesImported: 13000, watchStatesMerged: 60, ratingsFetched: 0, ratingsImported: 0, ratingsMerged: 0 }))
            .toBe('Watch states: 13,000 titles from 13,360 Plex entries, 60 shared a title, 300 not found in Vora');
    });

    it('leaves out a kind that was not imported and the note when nothing was shared', () => {
        const watchedOnly = { ...counts, watchStatesMerged: 0, watchStatesImported: 1, watchStatesFetched: 1, ratingsFetched: 0, ratingsImported: 0, ratingsMerged: 0 };
        expect(migrationSummary(watchedOnly, 'Watch history')).toBe('Watch history: 1 title from 1 Plex entry, none missing');
        expect(sharedTitleNote(watchedOnly)).toBeNull();
    });
});

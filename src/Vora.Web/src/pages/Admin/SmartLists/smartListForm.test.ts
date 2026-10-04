import { describe, it, expect } from 'vitest';
import type { SmartListAdminDto } from '../../../api/Collections/smartListService';
import { describeList, emptyForm, formFromList, requestFromForm, withSource } from './smartListForm';

const list = (overrides: Partial<SmartListAdminDto> = {}): SmartListAdminDto => ({
    id: 'l1',
    title: 'Holiday Movies',
    source: 'Library',
    defaultKey: null,
    libraryId: 'lib-1',
    filterRulesJson: JSON.stringify({ mediaTypes: ['Movie'], decade: 2000, genreIds: [10751], contentRating: 'PG', unwatchedOnly: true }),
    sortBy: 'ReleaseDateAsc',
    maxItems: 30,
    displayOrder: 4,
    showOnHomepage: true,
    showToFriends: false,
    activeStartMonth: 12,
    activeStartDay: 1,
    activeEndMonth: 1,
    activeEndDay: 6,
    collectionId: null,
    ...overrides,
});

describe('smart list form', () => {
    it('saves an edited list without losing the rules, library or season it had', () => {
        const saved = requestFromForm({ ...formFromList(list()), title: 'Holiday Films' }, 4);

        expect(saved).toMatchObject({
            title: 'Holiday Films',
            source: 'Library',
            libraryId: 'lib-1',
            sortBy: 'ReleaseDateAsc',
            maxItems: 30,
            showToFriends: false,
            activeStartMonth: 12,
            activeStartDay: 1,
            activeEndMonth: 1,
            activeEndDay: 6,
        });
        expect(JSON.parse(saved.filterRulesJson)).toEqual({ mediaTypes: ['Movie'], decade: 2000, genreIds: [10751], contentRating: 'PG', unwatchedOnly: true });
    });

    it('drops the season window when it is switched off', () => {
        const saved = requestFromForm({ ...formFromList(list()), seasonal: false }, 0);

        expect(saved.activeStartMonth).toBeNull();
        expect(saved.activeEndDay).toBeNull();
    });

    it('keeps a day that exists in the month', () => {
        const saved = requestFromForm({ ...emptyForm(), title: 'x', seasonal: true, startMonth: 2, startDay: 31, endMonth: 3, endDay: 1 }, 0);

        expect(saved.activeStartDay).toBe(29);
    });

    it('clamps the size to what the server accepts', () => {
        expect(requestFromForm({ ...emptyForm(), title: 'x', maxItems: 900 }, 0).maxItems).toBe(100);
        expect(requestFromForm({ ...emptyForm(), title: 'x', maxItems: 0 }, 0).maxItems).toBe(1);
    });

    it('a collection row sends the collection and no rules', () => {
        const saved = requestFromForm({ ...formFromList(list()), mode: 'collection', collectionId: 'c1' }, 0);

        expect(saved.collectionId).toBe('c1');
        expect(saved.libraryId).toBeNull();
        expect(saved.filterRulesJson).toBe('{}');
    });

    it('switching to a source that cannot use the sort picks its first one', () => {
        const form = withSource({ ...emptyForm(), sortBy: 'MostWatched', libraryId: 'lib-1' }, 'FavoriteChannels');

        expect(form.sortBy).toBe('TitleAsc');
        expect(form.libraryId).toBe('');
    });

    it('podcast rows keep only their own options', () => {
        const form = { ...withSource({ ...emptyForm(), mediaTypes: ['Movie'], decade: '1990' }, 'NewPodcastEpisodes'), title: 'Pods', unwatchedOnly: true, days: '14' };

        expect(JSON.parse(requestFromForm(form, 0).filterRulesJson)).toEqual({ unwatchedOnly: true, days: 14 });
    });

    it('describes each kind of row', () => {
        const names = { collection: () => 'Christmas', library: () => 'Movies' };

        expect(describeList(list(), names.collection, names.library)).toBe('Movies · 2000s · 1 genre · Rated PG · Unwatched only · Movies · Dec 1 – Jan 6');
        expect(describeList(list({ source: 'NewPodcastEpisodes', libraryId: null, activeStartMonth: null, filterRulesJson: '{"unwatchedOnly":true,"days":14}' }), names.collection, names.library))
            .toBe('New podcast episodes · Unplayed only · Last 14 days');
        expect(describeList(list({ collectionId: 'c1', libraryId: null, activeStartMonth: null }), names.collection, names.library)).toBe('Collection: Christmas');
    });
});

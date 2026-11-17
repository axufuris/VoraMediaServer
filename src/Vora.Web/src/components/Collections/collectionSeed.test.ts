import { describe, it, expect } from 'vitest';
import type { CollectionDetails } from '../../api/Collections/collectionService';
import { blankSeed, cloneSeed, parseSmartDefinition, SMART_CONTENT } from './collectionSeed';

const source = (over: Partial<CollectionDetails> = {}): CollectionDetails => ({
    id: 'source',
    title: 'Halloween Movies',
    description: 'Scary season picks',
    posterUrl: '/api/artwork/vora/holidays/halloween.jpg?v=abc',
    backdropUrl: 'https://image.tmdb.org/t/p/original/backdrop.jpg',
    defaultSortName: 'Release Date',
    isMixedCollection: false,
    itemCount: 12,
    lockedFields: ['PosterUrl'],
    items: [],
    defaultSort: 'Chronological',
    libraryId: 'movies-1',
    sortProviderId: 'openai_chronology',
    externalListId: 'Halloween in story order',
    autoSyncChronology: true,
    sortTitle: '1_Halloween',
    visibleStartDate: '2000-10-01T00:00:00Z',
    visibleEndDate: '2000-10-31T00:00:00Z',
    visibleEveryYear: true,
    systemGenerated: false,
    contentSyncProviderId: 'openai_theme',
    contentSyncExternalId: 'Halloween',
    syncIntervalDays: 7,
    mirrorList: true,
    ...over,
});

const rules = JSON.stringify({
    root: {
        match: 'All',
        rules: [
            { field: 'Genre', operator: 'Contains', value: 'Horror' },
            { field: 'DurationSeconds', operator: 'LessThan', value: '7200' },
        ],
        groups: [],
    },
    limit: 500,
    sortBy: 'Title',
    sortDirection: 'Asc',
});

describe('cloneSeed', () => {
    it('carries every value the create dialog shows', () => {
        const seed = cloneSeed(source(), false, 'Movie');

        expect(seed).toMatchObject({
            title: 'Halloween Movies',
            description: 'Scary season picks',
            sortTitle: '1_Halloween',
            posterUrl: '/api/artwork/vora/holidays/halloween.jpg?v=abc',
            backdropUrl: 'https://image.tmdb.org/t/p/original/backdrop.jpg',
            defaultSort: 'Chronological',
            sortProviderId: 'openai_chronology',
            externalListId: 'Halloween in story order',
            autoSyncChronology: true,
            contentSyncProviderId: 'openai_theme',
            contentSyncExternalId: 'Halloween',
            syncIntervalDays: 7,
            mirrorList: true,
            lockedFields: ['PosterUrl'],
        });
        expect(seed.season).toMatchObject({ everyYear: true, startMonth: 10, startDay: 1, endMonth: 10, endDay: 31 });
    });

    it('treats the poster as chosen, so a title never swaps it for a suggestion, even when there is none', () => {
        expect(cloneSeed(source(), false, 'Movie').posterChosen).toBe(true);
        const bare = cloneSeed(source({ posterUrl: undefined, backdropUrl: undefined }), false, 'Movie');
        expect(bare).toMatchObject({ posterUrl: '', backdropUrl: '', posterChosen: true });
    });

    it('does not share the locked-fields list with the original', () => {
        const original = source();
        const seed = cloneSeed(original, false, 'Movie');

        seed.lockedFields.push('Description');

        expect(original.lockedFields).toEqual(['PosterUrl']);
    });

    it('fills blanks for a collection that has none of the optional values', () => {
        const seed = cloneSeed(source({
            description: undefined, sortTitle: undefined, sortProviderId: undefined, externalListId: undefined,
            contentSyncProviderId: undefined, contentSyncExternalId: undefined, visibleStartDate: undefined,
            visibleEndDate: undefined, visibleEveryYear: false, mirrorList: false, autoSyncChronology: false,
            syncIntervalDays: 0,
        }), false, 'Movie');

        expect(seed).toMatchObject({
            description: '', sortTitle: '', sortProviderId: '', externalListId: '', contentSyncProviderId: '',
            contentSyncExternalId: '', mirrorList: false, autoSyncChronology: false, syncIntervalDays: 30,
        });
        expect(seed.season).toMatchObject({ everyYear: false, startDate: '', endDate: '' });
    });

    it('makes a smart collection smart again, with its rules, limit and sort', () => {
        const seed = cloneSeed(source({ rulesJson: rules, contentSyncProviderId: undefined, smartMediaType: 'Movies' }), false, 'Movie');

        expect(seed.contentSyncProviderId).toBe(SMART_CONTENT);
        expect(seed.smartDefinition).toMatchObject({ limit: 500, sortBy: 'Title', sortDirection: 'Asc' });
        expect(seed.smartDefinition.root.rules.map(r => r.field)).toEqual(['Genre', 'DurationSeconds']);
    });

    it('drops the rules a show library has no field for, such as Duration', () => {
        const seed = cloneSeed(source({ rulesJson: rules, smartMediaType: 'Movies' }), false, 'TvShow');

        expect(seed.smartDefinition.root.rules.map(r => r.field)).toEqual(['Genre']);
    });

    it('keeps a global copy matching what the original matched, and Mixed when it never said', () => {
        expect(cloneSeed(source({ rulesJson: rules, smartMediaType: 'Shows' }), true).globalSmartMedia).toBe('Shows');
        expect(cloneSeed(source({ rulesJson: rules, smartMediaType: undefined }), true).globalSmartMedia).toBe('Mixed');
    });

    it('keeps Duration on a global copy that matches movies', () => {
        const seed = cloneSeed(source({ rulesJson: rules, smartMediaType: 'Movies' }), true);

        expect(seed.smartDefinition.root.rules.map(r => r.field)).toEqual(['Genre', 'DurationSeconds']);
    });
});

describe('parseSmartDefinition', () => {
    it('falls back to the default rules for missing or broken JSON', () => {
        expect(parseSmartDefinition(undefined)).toMatchObject({ limit: 2000, sortBy: 'DateAdded' });
        expect(parseSmartDefinition('{nope')).toMatchObject({ limit: 2000, sortBy: 'DateAdded' });
        expect(parseSmartDefinition('{"limit":5}')).toMatchObject({ limit: 2000 });
    });
});

describe('blankSeed', () => {
    it('is what a new collection starts from', () => {
        expect(blankSeed()).toMatchObject({
            title: '', posterChosen: false, defaultSort: 'ReleaseDateAsc', syncIntervalDays: 30,
            contentSyncProviderId: '', globalSmartMedia: 'Mixed', lockedFields: [],
        });
    });
});

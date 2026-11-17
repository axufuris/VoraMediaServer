import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { SmartPlaylistOperator, SmartPlaylistRule, SmartPlaylistRuleGroup } from '../../api/Music/smartPlaylistService';
import { collectionFieldsFor, pruneRules, type RuleFieldKind } from './smartCollectionMedia';
import {
    findSmartTemplate,
    SMART_COLLECTION_TEMPLATES,
    SMART_TEMPLATE_GROUPS,
    templatesInGroup,
    type SmartCollectionTemplate,
} from './smartCollectionTemplates';

interface PosterList {
    categories: { id: string; name: string }[];
    posters: { id: string; category: string; title: string }[];
}

const posters: PosterList = JSON.parse(readFileSync(
    resolve(process.cwd(), '..', 'Vora.Application', 'Assets', 'Posters', 'posters.json'),
    'utf-8',
));

const TODAY = new Date(2026, 5, 15);

const rulesOf = (group: SmartPlaylistRuleGroup): SmartPlaylistRule[] => [...group.rules, ...group.groups.flatMap(rulesOf)];
const build = (id: string) => (findSmartTemplate(id) as SmartCollectionTemplate).build(TODAY);

const OPERATORS: Record<RuleFieldKind, SmartPlaylistOperator[]> = {
    string: ['Equals', 'NotEquals', 'Contains', 'NotContains', 'StartsWith', 'EndsWith', 'IsNull', 'IsNotNull'],
    int: ['Equals', 'NotEquals', 'GreaterThan', 'LessThan', 'Between', 'IsNull', 'IsNotNull'],
    date: ['InLastDays', 'NotInLastDays', 'GreaterThan', 'LessThan', 'IsNull', 'IsNotNull'],
    bool: ['Equals'],
};

describe('smart collection templates', () => {
    it('has one for every Vora poster, and none for a poster that does not exist', () => {
        expect(SMART_COLLECTION_TEMPLATES.map(t => t.id).sort()).toEqual(posters.posters.map(p => p.id).sort());
    });

    it('files each under the same group as its poster, in the poster order', () => {
        for (const category of posters.categories) {
            expect(templatesInGroup(category.name as never).map(t => t.id)).toEqual(
                posters.posters.filter(p => p.category === category.id).map(p => p.id),
            );
        }
        expect(SMART_TEMPLATE_GROUPS).toEqual(posters.categories.map(c => c.name));
    });

    it('names each by its poster', () => {
        for (const poster of posters.posters.filter(p => ['holidays', 'genres', 'popular'].includes(p.category))) {
            const label = (findSmartTemplate(poster.id) as SmartCollectionTemplate).label;
            expect(label.replace(/^Rated /, '').toLowerCase()).toBe(poster.title.toLowerCase());
        }
    });

    it('has a unique id and label within each group', () => {
        const ids = SMART_COLLECTION_TEMPLATES.map(t => t.id);
        expect(new Set(ids).size).toBe(ids.length);
        for (const name of SMART_TEMPLATE_GROUPS) {
            const labels = templatesInGroup(name).map(t => t.label);
            expect(new Set(labels).size).toBe(labels.length);
        }
    });

    describe.each(SMART_COLLECTION_TEMPLATES.map(t => [t.id, t] as const))('%s', (_id, template) => {
        const root = template.build(TODAY);
        const rules = rulesOf(root);

        it('has rules to match with', () => {
            expect(rules.length).toBeGreaterThan(0);
            expect(['All', 'Any']).toContain(root.match);
        });

        it.each(['Movies', 'Shows', 'Mixed'] as const)('uses only fields and operators the editor offers a %s collection', media => {
            const fields = collectionFieldsFor(media);
            for (const r of rules) {
                const def = fields.find(f => f.value === r.field);
                expect(def, `${r.field} is not a ${media} collection field`).toBeDefined();
                expect(OPERATORS[def!.kind], `${r.operator} on ${r.field}`).toContain(r.operator);
            }
        });

        it('survives pruning for any kind of collection', () => {
            for (const media of ['Movies', 'Shows', 'Mixed'] as const) {
                expect(pruneRules(template.build(TODAY), media)).toEqual(template.build(TODAY));
            }
        });

        it('gives every rule the value its operator needs', () => {
            for (const r of rules) {
                expect((r.value ?? '').trim().length).toBeGreaterThan(0);
                if (r.operator === 'Between') {
                    expect(Number.isFinite(Number(r.value))).toBe(true);
                    expect(Number.isFinite(Number(r.secondValue))).toBe(true);
                    expect(Number(r.secondValue)).toBeGreaterThanOrEqual(Number(r.value));
                } else {
                    expect(r.secondValue).toBeUndefined();
                }
                if (['int', 'date'].includes(collectionFieldsFor('Mixed').find(f => f.value === r.field)!.kind) && r.operator !== 'Between') {
                    expect(Number.isInteger(Number(r.value))).toBe(true);
                }
            }
        });

        it('builds a fresh set each time, so editing one collection cannot change the template', () => {
            const first = template.build(TODAY);
            const second = template.build(TODAY);

            expect(second).toEqual(first);
            expect(second).not.toBe(first);
            expect(second.rules).not.toBe(first.rules);
            if (first.rules[0]) expect(second.rules[0]).not.toBe(first.rules[0]);
        });
    });

    describe('genres', () => {
        it.each([
            ['action', 'Action'], ['adventure', 'Adventure'], ['animation', 'Animation'], ['comedy', 'Comedy'], ['crime', 'Crime'],
            ['documentary', 'Documentary'], ['drama', 'Drama'], ['family', 'Family'], ['fantasy', 'Fantasy'], ['history', 'History'],
            ['horror', 'Horror'], ['music', 'Music'], ['mystery', 'Mystery'], ['reality', 'Reality'], ['romance', 'Romance'],
            ['thriller', 'Thriller'], ['war', 'War'], ['western', 'Western'], ['anime', 'Anime'],
        ])('%s matches a title whose genre contains %s', (id, name) => {
            expect(build(id)).toEqual({ match: 'Any', groups: [], rules: [{ field: 'Genre', operator: 'Contains', value: name }] });
        });

        it('matches television\'s own names for a genre as well as the film ones', () => {
            const values = (id: string) => rulesOf(build(id)).map(r => r.value);

            expect(values('science-fiction')).toEqual(['Science Fiction', 'Sci-Fi']);
            expect(values('action')).toEqual(['Action']);
            expect(values('war')).toEqual(['War']);
        });

        it('takes a kids title by genre or by a children\'s rating', () => {
            expect(build('kids')).toEqual({
                match: 'Any',
                groups: [],
                rules: [
                    { field: 'Genre', operator: 'Contains', value: 'Kids' },
                    { field: 'Genre', operator: 'Contains', value: 'Children' },
                    { field: 'ContentRating', operator: 'Equals', value: 'TV-Y' },
                    { field: 'ContentRating', operator: 'Equals', value: 'TV-Y7' },
                    { field: 'ContentRating', operator: 'Equals', value: 'G' },
                ],
            });
        });

        it('looks for a few well-known heroes as well as the genre Superhero', () => {
            const values = rulesOf(build('superhero')).map(r => r.value);

            expect(values).toContain('Superhero');
            expect(values).toEqual(expect.arrayContaining(['Batman', 'Spider-Man', 'Avengers']));
        });
    });

    describe('decades', () => {
        it.each([1920, 1930, 1940, 1950, 1960, 1970, 1980, 1990, 2000, 2010, 2020])('%is is that year through the ninth', start => {
            expect(build(`${start}s`)).toEqual({
                match: 'All',
                groups: [],
                rules: [{ field: 'ReleaseYear', operator: 'Between', value: String(start), secondValue: String(start + 9) }],
            });
        });
    });

    describe('ratings', () => {
        it.each([
            ['g', 'G'], ['pg', 'PG'], ['pg-13', 'PG-13'], ['r', 'R'], ['nc-17', 'NC-17'],
            ['tv-y', 'TV-Y'], ['tv-y7', 'TV-Y7'], ['tv-g', 'TV-G'], ['tv-pg', 'TV-PG'], ['tv-14', 'TV-14'], ['tv-ma', 'TV-MA'],
        ])('%s is a content rating of exactly %s', (id, rating) => {
            expect(build(id)).toEqual({ match: 'All', groups: [], rules: [{ field: 'ContentRating', operator: 'Equals', value: rating }] });
        });
    });

    describe('holidays', () => {
        const holidays = templatesInGroup('Holidays');

        it('has the twenty holidays, each matching on any of its words', () => {
            expect(holidays).toHaveLength(20);
            for (const template of holidays) expect(template.build(TODAY).match).toBe('Any');
        });

        it('takes horror as well as the title for Halloween', () => {
            expect(build('halloween').rules).toEqual(expect.arrayContaining([
                { field: 'Genre', operator: 'Contains', value: 'Horror' },
                { field: 'Title', operator: 'Contains', value: 'Halloween' },
            ]));
        });

        it('finds Christmas by its words in the title', () => {
            const values = rulesOf(build('christmas')).map(r => r.value);

            expect(values).toEqual(expect.arrayContaining(['Christmas', 'Xmas', 'Santa']));
        });

        it('takes war titles for the two remembrance days', () => {
            for (const id of ['memorial-day', 'veterans-day']) {
                expect(build(id).rules).toContainEqual({ field: 'Genre', operator: 'Contains', value: 'War' });
            }
        });

        it('avoids short words that sit inside others, such as Mom inside Moment and Tree inside Street', () => {
            const values = holidays.flatMap(t => rulesOf(t.build(TODAY)).map(r => r.value ?? ''));

            expect(values).not.toContain('Mom');
            expect(values).not.toContain('Tree');
        });

        it('tells the admin the title match is thin, and points to AI Theme', () => {
            for (const template of holidays) expect(template.note).toMatch(/AI Theme/);
        });
    });

    describe('popular', () => {
        it('takes recently added from the days since a title was added', () => {
            expect(build('recently-added')).toEqual({
                match: 'All', groups: [], rules: [{ field: 'DateAdded', operator: 'InLastDays', value: '30' }],
            });
        });

        it('takes new releases as last year and this year, as of the date given', () => {
            expect(build('new-releases').rules).toEqual([{ field: 'ReleaseYear', operator: 'Between', value: '2025', secondValue: '2026' }]);
            expect((findSmartTemplate('new-releases') as SmartCollectionTemplate).build(new Date(2030, 0, 2)).rules[0])
                .toMatchObject({ value: '2029', secondValue: '2030' });
        });

        it('takes popular and top rated by the audience rating', () => {
            expect(build('popular').rules).toEqual([{ field: 'AudienceRating', operator: 'GreaterThan', value: '7' }]);
            expect(build('top-rated').rules).toEqual([{ field: 'AudienceRating', operator: 'GreaterThan', value: '8' }]);
        });

        it('takes trending as well-rated and recently added, both required', () => {
            const trending = build('trending');

            expect(trending.match).toBe('All');
            expect(trending.rules).toEqual([
                { field: 'AudienceRating', operator: 'GreaterThan', value: '7' },
                { field: 'DateAdded', operator: 'InLastDays', value: '90' },
            ]);
        });

        it('says plainly, for the ones Vora has no data for, what it matches instead', () => {
            for (const id of ['trending', 'award-winners', 'hidden-gems', 'blockbusters']) {
                expect((findSmartTemplate(id) as SmartCollectionTemplate).note).toMatch(/Vora (has no|can't tell)/);
            }
        });

        it('requires a rating and one of the right genres for award winners and blockbusters', () => {
            for (const id of ['award-winners', 'blockbusters']) {
                const root = build(id);
                expect(root.match).toBe('All');
                expect(root.rules[0]).toMatchObject({ field: 'AudienceRating', operator: 'GreaterThan' });
                expect(root.groups).toHaveLength(1);
                expect(root.groups[0].match).toBe('Any');
                expect(root.groups[0].rules.every(r => r.field === 'Genre')).toBe(true);
            }
        });
    });

    describe('lookup', () => {
        it('finds a template by id, and nothing for an unknown id', () => {
            expect(findSmartTemplate('halloween')?.label).toBe('Halloween');
            expect(findSmartTemplate('nope')).toBeUndefined();
        });
    });
});

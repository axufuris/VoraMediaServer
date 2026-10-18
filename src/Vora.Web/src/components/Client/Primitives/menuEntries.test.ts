import { describe, expect, it } from 'vitest';
import { menuAction, menuHeading, menuSeparator, tidyMenu } from './menuEntries';

const noop = () => undefined;

describe('tidyMenu', () => {
    it('keeps a heading over the group it names', () => {
        const entries = tidyMenu([menuHeading('episode', 'S1 E2'), menuAction('edit', 'Edit metadata', noop), menuSeparator('s'), menuHeading('season', 'Season 1'), menuAction('rate', 'Rate Season 1', noop)]);

        expect(entries.map(entry => entry.key)).toEqual(['episode', 'edit', 's', 'season', 'rate']);
    });

    it('drops a heading with nothing under it, before a separator or at the end', () => {
        const entries = tidyMenu([menuHeading('episode', 'S1 E2'), menuSeparator('s'), menuHeading('season', 'Season 1'), menuAction('rate', 'Rate Season 1', noop), menuHeading('empty', 'Nothing')]);

        expect(entries.map(entry => entry.key)).toEqual(['season', 'rate']);
    });
});

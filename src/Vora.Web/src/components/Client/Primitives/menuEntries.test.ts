import { describe, expect, it } from 'vitest';
import { menuAction, menuHeading, menuSeparator, menuSubmenu, tidyMenu, withKeyPrefix, type MenuEntry } from './menuEntries';

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

describe('a submenu', () => {
    it('is tidied inside, and dropped when nothing is left in it', () => {
        const entries = tidyMenu([
            menuAction('edit', 'Edit metadata', noop),
            menuSubmenu('manage', 'Manage movie…', [menuSeparator('a'), menuAction('refresh', 'Refresh metadata', noop), menuSeparator('b')]),
            menuSeparator('c'),
            menuSubmenu('empty', 'Manage nothing…', [menuSeparator('d')]),
        ]);

        expect(entries.map(entry => entry.key)).toEqual(['edit', 'manage']);
        const manage = entries[1] as Extract<MenuEntry, { kind: 'submenu' }>;
        expect(manage.entries.map(entry => entry.key)).toEqual(['refresh']);
    });

    it("drops a heading whose only entry was an empty submenu", () => {
        const entries = tidyMenu([menuHeading('season', 'Season 1'), menuSubmenu('manage', 'Manage season…', [])]);

        expect(entries).toEqual([]);
    });
});

describe('withKeyPrefix', () => {
    it("keeps two titles' entries apart, inside their submenus too", () => {
        const prefixed = withKeyPrefix('season', [menuAction('edit', 'Edit metadata', noop), menuSubmenu('manage', 'Manage season…', [menuAction('refresh', 'Refresh metadata', noop)])]);

        expect(prefixed.map(entry => entry.key)).toEqual(['season-edit', 'season-manage']);
        const manage = prefixed[1] as Extract<MenuEntry, { kind: 'submenu' }>;
        expect(manage.entries.map(entry => entry.key)).toEqual(['season-refresh']);
    });
});

export interface MenuPoint {
    x: number;
    y: number;
}

export type MenuEntry =
    | { kind: 'action'; key: string; label: string; onSelect: () => void; tone?: 'danger' }
    | { kind: 'choice'; key: string; label: string; detail?: string; selected: boolean; onSelect: () => void }
    | { kind: 'separator'; key: string }
    | { kind: 'heading'; key: string; label: string }
    | { kind: 'submenu'; key: string; label: string; entries: MenuEntry[] };

export const menuAction = (key: string, label: string, onSelect: () => void, tone?: 'danger'): MenuEntry =>
    ({ kind: 'action', key, label, onSelect, tone });

// One of several options where the one in use is marked, like the audio
// tracks of a title.
export const menuChoice = (key: string, label: string, selected: boolean, onSelect: () => void, detail?: string): MenuEntry =>
    ({ kind: 'choice', key, label, detail, selected, onSelect });

export const menuSeparator = (key: string): MenuEntry => ({ kind: 'separator', key });

// Names the group below it, where one menu holds the actions of two titles
// (a season page's selected episode, then the season).
export const menuHeading = (key: string, label: string): MenuEntry => ({ kind: 'heading', key, label });

// A row that opens more rows in the same menu, with a Back row above them, so
// a long run of rarely used actions (an admin's refresh, scan, thumbnails…)
// takes one line until it is wanted. A list in place rather than a fly-out, so
// a TV remote moves through it the same way.
export const menuSubmenu = (key: string, label: string, entries: MenuEntry[]): MenuEntry => ({ kind: 'submenu', key, label, entries });

// One menu that holds the entries of two titles (a season page's episode, then
// the season) needs each title's keys kept apart: both have an "edit", and both
// a "manage" submenu that must open its own rows.
export const withKeyPrefix = (prefix: string, entries: MenuEntry[]): MenuEntry[] =>
    entries.map(entry => entry.kind === 'submenu'
        ? { ...entry, key: `${prefix}-${entry.key}`, entries: withKeyPrefix(prefix, entry.entries) }
        : { ...entry, key: `${prefix}-${entry.key}` });

// Groups are built with a separator between them whether or not a group ends up
// with anything in it, so drop the ones that would open the menu, close it, or
// sit against another separator, a heading with nothing under it, and a submenu
// with nothing in it.
export function tidyMenu(entries: MenuEntry[]): MenuEntry[] {
    const kept = entries
        .map(entry => entry.kind === 'submenu' ? { ...entry, entries: tidyMenu(entry.entries) } : entry)
        .filter(entry => entry.kind !== 'submenu' || entry.entries.length > 0);
    const tidy: MenuEntry[] = [];
    kept.forEach((entry, index) => {
        const previous = tidy[tidy.length - 1];
        if (entry.kind === 'separator' && (!previous || previous.kind === 'separator')) return;
        if (entry.kind === 'heading') {
            const next = kept[index + 1];
            if (!next || next.kind === 'separator' || next.kind === 'heading') return;
        }
        tidy.push(entry);
    });
    while (tidy.length > 0 && tidy[tidy.length - 1].kind === 'separator') tidy.pop();
    return tidy;
}

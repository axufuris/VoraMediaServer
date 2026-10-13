export interface MenuPoint {
    x: number;
    y: number;
}

export type MenuEntry =
    | { kind: 'action'; key: string; label: string; onSelect: () => void; tone?: 'danger' }
    | { kind: 'separator'; key: string };

export const menuAction = (key: string, label: string, onSelect: () => void, tone?: 'danger'): MenuEntry =>
    ({ kind: 'action', key, label, onSelect, tone });

export const menuSeparator = (key: string): MenuEntry => ({ kind: 'separator', key });

// Groups are built with a separator between them whether or not a group ends up
// with anything in it, so drop the ones that would open the menu, close it, or
// sit against another separator.
export function tidyMenu(entries: MenuEntry[]): MenuEntry[] {
    const tidy: MenuEntry[] = [];
    for (const entry of entries) {
        const previous = tidy[tidy.length - 1];
        if (entry.kind === 'separator' && (!previous || previous.kind === 'separator')) continue;
        tidy.push(entry);
    }
    while (tidy.length > 0 && tidy[tidy.length - 1].kind === 'separator') tidy.pop();
    return tidy;
}

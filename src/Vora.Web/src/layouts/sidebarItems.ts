import { createContext, useContext } from 'react';
import type { NavItem } from './MainLayout';

export interface SidebarItems {
    items: NavItem[];
    loaded: boolean;
}

// The sidebar's entries as this profile arranged them on this device, so a page
// can leave out what the profile has unpinned there. Outside MainLayout there is
// no sidebar, so nothing counts as hidden.
export const SidebarItemsContext = createContext<SidebarItems>({ items: [], loaded: true });

export const useSidebarItems = (): SidebarItems => useContext(SidebarItemsContext);

// Music libraries have no sidebar entries of their own: the one Music entry
// opens all of them, so unpinning it hides every music library.
export function isLibraryUnpinned(items: NavItem[], library: { id: string; type: string }): boolean {
    const entry = library.type === 'Music'
        ? items.find(item => item.type === 'system' && item.id === 'music')
        : items.find(item => item.type === 'library' && item.id === library.id);
    return entry !== undefined && !entry.isPinned;
}

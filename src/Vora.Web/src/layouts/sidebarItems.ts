import { createContext, useContext, useMemo } from 'react';
import type { NavItem } from './MainLayout';
import { serverVault } from '../utils/serverVault';

export interface SidebarItems {
    items: NavItem[];
    loaded: boolean;
}

// The sidebar's entries as this profile arranged them on this device, so a page
// can leave out what the profile has unpinned there. Outside MainLayout there is
// no sidebar, so nothing counts as hidden.
export const SidebarItemsContext = createContext<SidebarItems>({ items: [], loaded: true });

export const useSidebarItems = (): SidebarItems => useContext(SidebarItemsContext);

export function isLibraryUnpinned(items: NavItem[], library: { id: string }): boolean {
    const entry = items.find(item => item.type === 'library' && item.id === library.id);
    return entry !== undefined && !entry.isPinned;
}

export function unpinnedLibraryIds(items: NavItem[], serverId: string | undefined): string[] {
    return items
        .filter(item => item.type === 'library' && !item.isPinned && item.serverId === serverId)
        .map(item => item.id)
        .sort();
}

export function useUnpinnedLibraryIds(serverId?: string): string[] | null {
    const { items, loaded } = useSidebarItems();
    const activeServerId = serverId ?? serverVault.getActiveServerId() ?? undefined;
    const key = loaded ? unpinnedLibraryIds(items, activeServerId).join(',') : null;
    return useMemo(() => (key === null ? null : key === '' ? [] : key.split(',')), [key]);
}

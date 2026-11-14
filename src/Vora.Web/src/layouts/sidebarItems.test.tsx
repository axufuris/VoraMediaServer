import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import type { NavItem } from './MainLayout';
import { SidebarItemsContext, type SidebarItems, unpinnedLibraryIds, useUnpinnedLibraryIds } from './sidebarItems';
import { serverVault } from '../utils/serverVault';

const library = (id: string, serverId: string, isPinned: boolean, order = 0): NavItem =>
    ({ id, serverId, serverName: serverId, title: id, path: `/server/${serverId}/library/${id}`, type: 'library', isPinned, order });

const system = (id: string, isPinned: boolean): NavItem =>
    ({ id, title: id, path: `/${id}`, type: 'system', isPinned, order: 0 });

const sidebar = [
    library('movies', 'home', true),
    library('test-shows', 'home', false),
    library('test-movies', 'home', false),
    library('kids', 'cabin', false),
    system('livetv', false),
];

const renderUnpinned = (value: SidebarItems, serverId?: string) => {
    const state = { value };
    const wrapper = ({ children }: { children: ReactNode }) =>
        <SidebarItemsContext.Provider value={state.value}>{children}</SidebarItemsContext.Provider>;
    const hook = renderHook(() => useUnpinnedLibraryIds(serverId), { wrapper });
    return {
        ...hook,
        update: (next: SidebarItems) => {
            state.value = next;
            hook.rerender();
        },
    };
};

beforeEach(() => localStorage.clear());
afterEach(() => localStorage.clear());

describe('unpinnedLibraryIds', () => {
    it('lists only the libraries unpinned on the given server', () => {
        expect(unpinnedLibraryIds(sidebar, 'home')).toEqual(['test-movies', 'test-shows']);
        expect(unpinnedLibraryIds(sidebar, 'cabin')).toEqual(['kids']);
        expect(unpinnedLibraryIds(sidebar, undefined)).toEqual([]);
    });
});

describe('useUnpinnedLibraryIds', () => {
    it('is unknown until the sidebar is in', () => {
        const { result, update } = renderUnpinned({ items: [], loaded: false }, 'home');

        expect(result.current).toBeNull();

        update({ items: sidebar, loaded: true });

        expect(result.current).toEqual(['test-movies', 'test-shows']);
    });

    it('reads the active server when the route names none', () => {
        serverVault.setActiveServerId('cabin');

        const { result } = renderUnpinned({ items: sidebar, loaded: true });

        expect(result.current).toEqual(['kids']);
    });

    it('leaves nothing out outside the client layout', () => {
        const { result } = renderHook(() => useUnpinnedLibraryIds('home'));

        expect(result.current).toEqual([]);
    });

    it('keeps the same list while the unpinned set is the same, and a new one when it changes', () => {
        const { result, update } = renderUnpinned({ items: sidebar, loaded: true }, 'home');
        const first = result.current;

        update({ items: [...sidebar].reverse().map((item, order) => ({ ...item, order })), loaded: true });
        expect(result.current).toBe(first);

        update({ items: sidebar.map(item => item.id === 'movies' ? { ...item, isPinned: false } : item), loaded: true });
        expect(result.current).toEqual(['movies', 'test-movies', 'test-shows']);
    });
});

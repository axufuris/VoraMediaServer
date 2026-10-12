import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import HistoryPage from './HistoryPage';
import type { HistorySessionDto } from '../../api/Media/historyService';
import type { UserVM } from '../../api/Users/userService';

const mocks = vi.hoisted(() => ({
    getHistory: vi.fn<(page: number, pageSize: number, search: string, profileId?: string, serverId?: string) => Promise<{ data: HistorySessionDto[]; total: number }>>(),
    getAllUsers: vi.fn<(serverId?: string) => Promise<UserVM[]>>(),
}));

vi.mock('../../api/Media/historyService', () => ({
    historyService: { getHistory: (page: number, pageSize: number, search: string, profileId?: string, serverId?: string) => mocks.getHistory(page, pageSize, search, profileId, serverId) },
}));
vi.mock('../../api/Media/libraryService', () => ({
    libraryService: { getLibraries: () => Promise.resolve([{ id: 'movies', name: 'Movies' }]) },
}));
vi.mock('../../api/Users/userService', () => ({
    userService: { getAllUsers: (serverId?: string) => mocks.getAllUsers(serverId) },
}));

const account = (id: string, displayName: string, profiles: [string, string][]) =>
    ({ id, displayName, profiles: profiles.map(([profileId, name]) => ({ id: profileId, name })) }) as UserVM;

const row = (userName: string, accountName: string): HistorySessionDto => ({
    id: `${userName}-row`,
    date: '2026-10-06T03:16:54Z',
    userName,
    accountName,
    ipAddress: '192.168.0.1',
    platform: 'Vora Web',
    product: 'Vora',
    player: 'Web Browser',
    title: 'Spider-Man: Brand New Day',
    mediaType: 'Movie',
    libraryId: 'movies',
    libraryName: 'Movies',
    strategy: 'DirectPlay',
    videoStrategy: 'DirectPlay',
    audioStrategy: 'DirectPlay',
    bandwidthKbps: 9800,
    startedAt: '2026-10-06T03:16:54Z',
    pausedMinutes: 22,
    stoppedAt: '2026-10-06T05:55:26Z',
    durationMinutes: 137,
    percentComplete: 99,
    isGrouped: false,
});

describe('HistoryPage', () => {
    beforeEach(() => {
        mocks.getHistory.mockReset().mockResolvedValue({ data: [row('AX Media Server', 'Andy'), row('Danold', 'Danold')], total: 2 });
        mocks.getAllUsers.mockReset().mockResolvedValue([
            account('andy', 'Andy', [['admin-profile', 'AX Media Server'], ['kids-profile', 'Kids']]),
            account('danold', 'Danold', [['danold-profile', 'Danold']]),
        ]);
    });

    it('lists every profile under its account, not just the ones on the page', async () => {
        render(<MemoryRouter><HistoryPage /></MemoryRouter>);

        const select = screen.getByRole('combobox', { name: 'Profile' });
        await waitFor(() => expect(within(select).getByRole('option', { name: 'Kids' })).toBeInTheDocument());
        expect(within(select).getAllByRole('option').map(o => o.textContent)).toEqual(['All profiles', 'AX Media Server', 'Kids', 'Danold']);
        expect(within(select).getByRole('group', { name: 'Andy' })).toBeInTheDocument();
    });

    it('asks the server for one profile when one is picked', async () => {
        render(<MemoryRouter><HistoryPage /></MemoryRouter>);
        const select = screen.getByRole('combobox', { name: 'Profile' });
        await waitFor(() => expect(within(select).getByRole('option', { name: 'Kids' })).toBeInTheDocument());

        fireEvent.change(select, { target: { value: 'kids-profile' } });

        await waitFor(() => expect(mocks.getHistory).toHaveBeenLastCalledWith(1, 25, '', 'kids-profile', undefined));
    });

    it('names the account under a profile only when the two differ', async () => {
        render(<MemoryRouter><HistoryPage /></MemoryRouter>);

        expect(await screen.findByTitle('Account')).toHaveTextContent('Andy');
        expect(screen.getAllByTitle('Account')).toHaveLength(1);
        expect(screen.getByRole('cell', { name: 'AX Media Server Andy' })).toBeInTheDocument();
    });
});

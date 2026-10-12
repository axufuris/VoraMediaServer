import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import HistoryPage from './HistoryPage';
import type { HistoryQuery, HistorySessionDto } from '../../api/Media/historyService';
import type { UserVM } from '../../api/Users/userService';

const mocks = vi.hoisted(() => ({
    getHistory: vi.fn<(query: HistoryQuery, serverId?: string) => Promise<{ data: HistorySessionDto[]; total: number }>>(),
    getAllUsers: vi.fn<(serverId?: string) => Promise<UserVM[]>>(),
}));

vi.mock('../../api/Media/historyService', () => ({
    historyService: { getHistory: (query: HistoryQuery, serverId?: string) => mocks.getHistory(query, serverId) },
}));
vi.mock('../../api/Media/libraryService', () => ({
    libraryService: { getLibraries: () => Promise.resolve([{ id: 'movies', name: 'Movies' }, { id: 'shows', name: 'Shows' }]) },
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

        await waitFor(() => expect(mocks.getHistory).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, pageSize: 25, profileId: 'kids-profile' }), undefined));
    });

    it('names the account under a profile only when the two differ', async () => {
        render(<MemoryRouter><HistoryPage /></MemoryRouter>);

        expect(await screen.findByTitle('Account')).toHaveTextContent('Andy');
        expect(screen.getAllByTitle('Account')).toHaveLength(1);
        expect(screen.getByRole('cell', { name: 'AX Media Server Andy' })).toBeInTheDocument();
    });

    it('pages through the history on the server', async () => {
        mocks.getHistory.mockResolvedValue({ data: [row('AX Media Server', 'Andy')], total: 60 });
        render(<MemoryRouter><HistoryPage /></MemoryRouter>);

        expect(await screen.findByText('Showing 1–25 of 60')).toBeInTheDocument();
        expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();

        fireEvent.click(screen.getByRole('button', { name: 'Next' }));

        await waitFor(() => expect(mocks.getHistory).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }), undefined));
        expect(await screen.findByText('Showing 26–50 of 60')).toBeInTheDocument();
    });

    it('filters by library and how it played on the server, back on the first page', async () => {
        mocks.getHistory.mockResolvedValue({ data: [row('AX Media Server', 'Andy')], total: 60 });
        render(<MemoryRouter><HistoryPage /></MemoryRouter>);
        await screen.findByText('Showing 1–25 of 60');
        fireEvent.click(screen.getByRole('button', { name: 'Next' }));
        await waitFor(() => expect(mocks.getHistory).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }), undefined));

        fireEvent.click(screen.getByRole('button', { name: 'Transcode' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Shows' }));

        await waitFor(() => expect(mocks.getHistory).toHaveBeenLastCalledWith(
            expect.objectContaining({ page: 1, libraryIds: ['movies'], deliveries: ['DirectPlay', 'DirectStream'] }),
            undefined));
    });

    it('asks for nothing when every way of playing is switched off', async () => {
        render(<MemoryRouter><HistoryPage /></MemoryRouter>);
        await screen.findByTitle('Account');
        const calls = mocks.getHistory.mock.calls.length;

        fireEvent.click(screen.getByRole('button', { name: 'Direct Play' }));
        fireEvent.click(screen.getByRole('button', { name: 'Direct Stream' }));
        fireEvent.click(screen.getByRole('button', { name: 'Transcode' }));

        expect(await screen.findByText('No matching history found.')).toBeInTheDocument();
        expect(mocks.getHistory.mock.calls.length).toBe(calls + 2);
    });

    it('leads an episode with its show, with the episode number and title beneath', async () => {
        mocks.getHistory.mockResolvedValue({
            data: [{ ...row('Andy', 'Andy'), id: 'ep', title: 'Ooze', showTitle: 'Haunted Hotel', seasonNumber: 2, episodeNumber: 1, mediaType: 'TvShow' }],
            total: 1,
        });

        render(<MemoryRouter><HistoryPage /></MemoryRouter>);

        const show = await screen.findByText('Haunted Hotel');
        const episode = screen.getByText('S02E01 · Ooze');
        expect(show.compareDocumentPosition(episode) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('puts the location under the address', async () => {
        mocks.getHistory.mockResolvedValue({
            data: [{ ...row('Andy', 'Andy'), ipAddress: '67.199.253.6', location: 'Salt Lake City, UT, US' }],
            total: 1,
        });

        render(<MemoryRouter><HistoryPage /></MemoryRouter>);

        const address = await screen.findByText('67.199.253.6');
        expect(within(address.parentElement as HTMLElement).getByText('Salt Lake City, UT, US')).toBeInTheDocument();
    });
});

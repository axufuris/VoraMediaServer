import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import DvrDashboard from './DvrDashboard';
import { serverVault } from '../../../utils/serverVault';
import { StorageKeys } from '../../../utils/storageKeys';
import { clearApiClientCache } from '../../../api/client';

const mocks = vi.hoisted(() => ({
    getRecordingSessions: vi.fn(),
    dvrSessionsUpdated: { current: null as (() => void) | null },
}));

vi.mock('../../../api/Iptv/dvrService', () => ({
    dvrService: { getRecordingSessions: mocks.getRecordingSessions },
}));
vi.mock('../../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia: vi.fn() }) }));
vi.mock('../../../hooks/useSignalREvent', () => ({
    useSignalREvent: (eventName: string, callback: () => void) => {
        if (eventName === 'DvrSessionsUpdated') mocks.dvrSessionsUpdated.current = callback;
    },
}));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const tokenFor = (profileId: string) => `header.${btoa(JSON.stringify({ sub: profileId }))}.signature`;

const renderAt = (path: string) => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/livetv" element={<DvrDashboard />} />
            <Route path="/server/:serverId/livetv" element={<DvrDashboard />} />
        </Routes>
    </MemoryRouter>,
);

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(StorageKeys.profileToken, tokenFor('profile-1'));
    serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'http://home', token: tokenFor('profile-1'), profileId: 'profile-1', isAdmin: false });
    serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: tokenFor('profile-2'), profileId: 'profile-2', isAdmin: false });
    serverVault.setActiveServerId('home');
    mocks.getRecordingSessions.mockResolvedValue([]);
});

afterEach(() => {
    cleanup();
    vi.clearAllMocks();
    localStorage.clear();
    clearApiClientCache();
});

describe('DvrDashboard on more than one server', () => {
    it("asks a second server for the recordings of that server's own profile", async () => {
        renderAt('/server/second/livetv');

        await waitFor(() => expect(mocks.getRecordingSessions).toHaveBeenCalledWith('profile-2', 'second'));
        expect(mocks.getRecordingSessions).not.toHaveBeenCalledWith('profile-1', expect.anything());
    });

    it('asks the primary server for the primary profile, as before', async () => {
        renderAt('/livetv');

        await waitFor(() => expect(mocks.getRecordingSessions).toHaveBeenCalledWith('profile-1', undefined));
    });

    it('refreshes from the active server with its own profile when recordings change', async () => {
        serverVault.setActiveServerId('second');
        renderAt('/server/second/livetv');
        await waitFor(() => expect(mocks.getRecordingSessions).toHaveBeenCalledTimes(1));

        mocks.dvrSessionsUpdated.current?.();

        await waitFor(() => expect(mocks.getRecordingSessions).toHaveBeenCalledTimes(2));
        expect(mocks.getRecordingSessions).toHaveBeenLastCalledWith('profile-2', 'second');
    });
});

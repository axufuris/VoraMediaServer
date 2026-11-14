import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ProfileHistoryPage from './ProfileHistoryPage';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys } from '../../utils/storageKeys';

const mocks = vi.hoisted(() => ({
    getPlayHistory: vi.fn(),
    getUserAccount: vi.fn(),
}));

vi.mock('../../api/Users/userService', () => ({
    userService: { getPlayHistory: mocks.getPlayHistory, getUserAccount: mocks.getUserAccount },
}));

const tokenFor = (profileId: string, accountId: string) => `header.${btoa(JSON.stringify({ sub: profileId, accountId }))}.signature`;

const renderAt = (path: string) => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/history" element={<ProfileHistoryPage />} />
            <Route path="/server/:serverId/history" element={<ProfileHistoryPage />} />
        </Routes>
    </MemoryRouter>,
);

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(StorageKeys.userId, 'account-1');
    localStorage.setItem(StorageKeys.profileToken, tokenFor('profile-1', 'account-1'));
    serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'http://home', token: tokenFor('profile-1', 'account-1'), profileId: 'profile-1', isAdmin: false });
    serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: tokenFor('profile-2', 'account-2'), profileId: 'profile-2', isAdmin: false });
    serverVault.setActiveServerId('home');
    mocks.getPlayHistory.mockResolvedValue({ data: [], total: 0 });
    mocks.getUserAccount.mockResolvedValue({ profiles: [] });
});

afterEach(() => {
    cleanup();
    vi.clearAllMocks();
    localStorage.clear();
});

describe('ProfileHistoryPage on more than one server', () => {
    it("asks a second server for the history of that server's own account and profile", async () => {
        renderAt('/server/second/history');

        await waitFor(() => expect(mocks.getPlayHistory).toHaveBeenCalled());
        expect(mocks.getPlayHistory).toHaveBeenCalledWith('account-2', 'profile-2', 1, 25, '', 'All', 'second');
    });

    it('asks the primary server for the primary account and profile, as before', async () => {
        renderAt('/history');

        await waitFor(() => expect(mocks.getPlayHistory).toHaveBeenCalled());
        expect(mocks.getPlayHistory).toHaveBeenCalledWith('account-1', 'profile-1', 1, 25, '', 'All', undefined);
    });

    it("lists a second server's profiles for its admin from that server's account", async () => {
        localStorage.setItem(StorageKeys.isServerAdmin, 'true');
        renderAt('/server/second/history');

        await waitFor(() => expect(mocks.getUserAccount).toHaveBeenCalledWith('account-2', 'second'));
    });
});

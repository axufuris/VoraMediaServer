import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { readLivePermissions, useLivePermissions } from './useLivePermissions';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys } from '../../utils/storageKeys';
import { clearApiClientCache } from '../../api/client';

const mocks = vi.hoisted(() => ({ getUserAccount: vi.fn() }));

vi.mock('../../api/Users/userService', () => ({ userService: { getUserAccount: mocks.getUserAccount } }));

const tokenFor = (claims: Record<string, string>) => `header.${btoa(JSON.stringify(claims))}.signature`;

const primaryToken = tokenFor({ sub: 'profile-1', accountId: 'account-1', isAdmin: 'True', canTimeshiftIptv: 'False', canRecordLiveTv: 'False' });
const secondToken = tokenFor({ sub: 'profile-2', accountId: 'account-2', isAdmin: 'False', canTimeshiftIptv: 'False', canRecordLiveTv: 'True' });

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(StorageKeys.profileToken, primaryToken);
    localStorage.setItem(StorageKeys.userId, 'account-1');
    localStorage.setItem(StorageKeys.isServerAdmin, 'true');
    serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'http://home', token: primaryToken, profileId: 'profile-1', isAdmin: true });
    serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: secondToken, profileId: 'profile-2', isAdmin: false });
    serverVault.setActiveServerId('home');
    mocks.getUserAccount.mockReturnValue(new Promise(() => { }));
});

afterEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    clearApiClientCache();
});

describe('live TV permissions on more than one server', () => {
    it('are the primary admin rights on the primary server, as before', () => {
        expect(readLivePermissions()).toEqual({ canTimeshift: true, canRecord: true });
    });

    it("come from the second server's token while it is the active server", () => {
        serverVault.setActiveServerId('second');

        expect(readLivePermissions()).toEqual({ canTimeshift: false, canRecord: true });
    });

    it('fall back to the stored admin flag when the token has no permission claims', () => {
        localStorage.setItem(StorageKeys.profileToken, 'opaque-token');
        serverVault.clearVault();

        expect(readLivePermissions()).toEqual({ canTimeshift: true, canRecord: true });
    });

    it("are refreshed from the second server with that server's own account", async () => {
        serverVault.setActiveServerId('second');
        mocks.getUserAccount.mockResolvedValue({ canTimeshiftIptv: true, canRecordLiveTv: false, isAdmin: false });

        const { result } = renderHook(() => useLivePermissions());

        await waitFor(() => expect(result.current).toEqual({ canTimeshift: true, canRecord: false }));
        expect(mocks.getUserAccount).toHaveBeenCalledWith('account-2', 'second');
    });
});

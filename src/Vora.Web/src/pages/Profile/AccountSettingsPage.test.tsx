import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import AccountSettingsPage from './AccountSettingsPage';
import { StorageKeys } from '../../utils/storageKeys';
import { serverVault } from '../../utils/serverVault';
import type { UserVM } from '../../api/Users/userService';

const getUserAccount = vi.fn<(userId: string, serverId?: string) => Promise<UserVM>>();

const account = {
    id: 'acct-1',
    email: 'andy@example.com',
    displayName: 'Andy',
    isAdmin: false,
    emailNotifyOnRequestAvailable: true,
    profiles: [{ id: 'profile-1', name: 'Andy', isAdmin: false, hasPin: false, accessSchedules: [] }],
} as unknown as UserVM;

vi.mock('../../api/Users/userService', () => ({
    userService: { getUserAccount: (userId: string, serverId?: string) => getUserAccount(userId, serverId), updateAccount: () => Promise.resolve() },
}));
vi.mock('../../api/Media/libraryService', () => ({
    libraryService: { getLibraries: () => Promise.resolve([]) },
}));
vi.mock('../../api/Users/profileService', () => ({
    profileService: { createProfile: () => Promise.resolve(), updateProfile: () => Promise.resolve(), deleteProfile: () => Promise.resolve() },
}));
vi.mock('../../api/Users/userImageService', () => ({
    userImageService: { uploadImage: () => Promise.resolve('') },
}));
vi.mock('../../api/Music/musicService', () => ({
    musicService: { startLastFmAuth: () => Promise.resolve({ authUrl: '', token: '' }), completeLastFmAuth: () => Promise.resolve(), disconnectLastFm: () => Promise.resolve() },
}));
const getPlaylists = vi.fn<(userId: string, profileId?: string, serverId?: string) => Promise<never[]>>(() => Promise.resolve([]));
vi.mock('../../api/Iptv/iptvClientService', () => ({
    iptvClientService: { getPlaylists: (userId: string, profileId?: string, serverId?: string) => getPlaylists(userId, profileId, serverId) },
}));
vi.mock('../../api/Auth/authService', () => ({
    authService: { getSetupStatus: () => Promise.resolve({ emailEnabled: false }) },
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const renderPage = () => render(
    <MemoryRouter>
        <div data-vora-client="">
            <AccountSettingsPage />
        </div>
    </MemoryRouter>,
);

describe('AccountSettingsPage', () => {
    beforeEach(() => {
        localStorage.clear();
        localStorage.setItem(StorageKeys.userId, 'acct-1');
        localStorage.setItem(StorageKeys.isProfileAdmin, 'true');
        getUserAccount.mockReset();
    });

    it('spins while the profile is on its way', async () => {
        let release: (value: UserVM) => void = () => { };
        getUserAccount.mockReturnValue(new Promise<UserVM>(resolve => { release = resolve; }));

        renderPage();

        expect(screen.getByRole('status')).toBeInTheDocument();
        expect(screen.getByText('Loading account settings…')).toBeInTheDocument();

        release(account);
        await waitFor(() => expect(screen.queryByRole('status')).toBeNull());
    });

    it('says what went wrong instead of spinning forever', async () => {
        getUserAccount.mockRejectedValue(new Error('offline'));
        vi.spyOn(console, 'error').mockImplementation(() => { });

        renderPage();

        const alert = await screen.findByRole('alert');
        expect(alert).toHaveTextContent('We could not load your profile');
        expect(screen.queryByRole('status')).toBeNull();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    });

    it('loads again when the viewer retries', async () => {
        getUserAccount.mockRejectedValueOnce(new Error('offline')).mockResolvedValue(account);
        vi.spyOn(console, 'error').mockImplementation(() => { });
        renderPage();

        (await screen.findByRole('button', { name: 'Try again' })).click();

        await waitFor(() => expect(screen.queryByRole('alert')).toBeNull());
        expect(getUserAccount).toHaveBeenCalledTimes(2);
    });

    it('renders the form once the profile arrives', async () => {
        getUserAccount.mockResolvedValue(account);

        renderPage();

        expect(await screen.findByDisplayValue('andy@example.com')).toBeInTheDocument();
        expect(screen.queryByRole('status')).toBeNull();
        expect(screen.queryByRole('alert')).toBeNull();
    });

    it('tells a signed-out device what is wrong rather than loading nothing', async () => {
        localStorage.removeItem(StorageKeys.userId);

        renderPage();

        expect(await screen.findByRole('alert')).toHaveTextContent('not signed in');
        expect(getUserAccount).not.toHaveBeenCalled();
    });
});

describe('AccountSettingsPage on a second server', () => {
    const tokenFor = (profileId: string, accountId: string) => `header.${btoa(JSON.stringify({ sub: profileId, accountId }))}.signature`;

    beforeEach(() => {
        localStorage.clear();
        localStorage.setItem(StorageKeys.userId, 'acct-1');
        localStorage.setItem(StorageKeys.isProfileAdmin, 'false');
        localStorage.setItem(StorageKeys.profileToken, tokenFor('profile-1', 'acct-1'));
        serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: tokenFor('profile-2', 'acct-2'), profileId: 'profile-2', isAdmin: false });
        getUserAccount.mockReset();
        getUserAccount.mockResolvedValue({
            ...account,
            profiles: [{ id: 'profile-2', name: 'Andy on Second', isAdmin: false, hasPin: false, accessSchedules: [] }],
        } as unknown as UserVM);
    });

    it("opens that server's own profile, with its connections, for a profile that is not the main one", async () => {
        render(
            <MemoryRouter initialEntries={['/server/second/account']}>
                <div data-vora-client="">
                    <Routes><Route path="/server/:serverId/account" element={<AccountSettingsPage />} /></Routes>
                </div>
            </MemoryRouter>,
        );

        expect(await screen.findByDisplayValue('Andy on Second')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Connections' })).toBeInTheDocument();
        expect(getUserAccount).toHaveBeenCalledWith('acct-2', 'second');
        expect(getUserAccount).not.toHaveBeenCalledWith('acct-1', expect.anything());
    });
});

describe('AccountSettingsPage under a server id', () => {
    const tokenFor = (profileId: string, accountId: string) => `header.${btoa(JSON.stringify({ sub: profileId, accountId }))}.signature`;

    beforeEach(() => {
        localStorage.clear();
        localStorage.setItem(StorageKeys.userId, 'acct-1');
        localStorage.setItem(StorageKeys.isProfileAdmin, 'true');
        serverVault.addOrUpdateServer({ id: 'server_1793842485331', name: 'Home', url: 'http://home', token: tokenFor('profile-1', 'acct-1'), profileId: 'profile-1', isAdmin: true });
        getUserAccount.mockReset();
        getUserAccount.mockResolvedValue(account);
        getPlaylists.mockClear();
    });

    // Back to client in the admin pages opens /server/<id>, and the profile menu keeps the id on the way to Account settings.
    it("asks for the account's playlists without passing the server id as a profile", async () => {
        render(
            <MemoryRouter initialEntries={['/server/server_1793842485331/account']}>
                <div data-vora-client="">
                    <Routes><Route path="/server/:serverId/account" element={<AccountSettingsPage />} /></Routes>
                </div>
            </MemoryRouter>,
        );

        expect(await screen.findByDisplayValue('andy@example.com')).toBeInTheDocument();
        expect(getPlaylists).toHaveBeenCalledWith('acct-1', undefined, 'server_1793842485331');
        expect(screen.queryByRole('alert')).toBeNull();
    });
});

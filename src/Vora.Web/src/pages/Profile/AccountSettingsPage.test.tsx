import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import AccountSettingsPage from './AccountSettingsPage';
import { StorageKeys } from '../../utils/storageKeys';
import type { UserVM } from '../../api/Users/userService';

const getUserAccount = vi.fn<() => Promise<UserVM>>();

const account = {
    id: 'acct-1',
    email: 'andy@example.com',
    displayName: 'Andy',
    isAdmin: false,
    emailNotifyOnRequestAvailable: true,
    profiles: [{ id: 'profile-1', name: 'Andy', isAdmin: false, hasPin: false, accessSchedules: [] }],
} as unknown as UserVM;

vi.mock('../../api/Users/userService', () => ({
    userService: { getUserAccount: () => getUserAccount(), updateAccount: () => Promise.resolve() },
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
vi.mock('../../api/Iptv/iptvClientService', () => ({
    iptvClientService: { getPlaylists: () => Promise.resolve([]) },
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

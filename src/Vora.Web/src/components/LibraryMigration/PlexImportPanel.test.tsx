import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import PlexImportPanel from './PlexImportPanel';
import { StorageKeys } from '../../utils/storageKeys';
import type { LibrarySyncTokenVM } from '../../api/LibraryMigration/libraryImportService';

const mocks = vi.hoisted(() => ({
    getProviders: vi.fn(),
    listAccounts: vi.fn(),
    listServers: vi.fn(),
    listLibraries: vi.fn(),
    runImport: vi.fn(),
    getUserAccount: vi.fn(),
}));

vi.mock('../../api/LibraryMigration/libraryImportService', () => ({
    libraryImportService: {
        getProviders: mocks.getProviders,
        listAccounts: mocks.listAccounts,
        listServers: mocks.listServers,
        listLibraries: mocks.listLibraries,
        runImport: mocks.runImport,
    },
}));
vi.mock('../../api/Users/userService', () => ({ userService: { getUserAccount: mocks.getUserAccount } }));
vi.mock('../../hooks/useSignalREvent', () => ({ useSignalREvent: () => undefined }));
vi.mock('./LibrarySyncPinModal', () => ({
    LibrarySyncPinModal: ({ isOpen, onAuthorized }: { isOpen: boolean; onAuthorized: (token: LibrarySyncTokenVM) => void }) =>
        isOpen ? <button type="button" onClick={() => onAuthorized({ accessToken: 'plex-token', username: 'andyx' })}>Approve on plex.tv</button> : null,
}));

const tokenFor = (profileId: string) => `x.${btoa(JSON.stringify({ sub: profileId }))}.y`;

function signIn({ profileAdmin, serverAdmin = false }: { profileAdmin: boolean; serverAdmin?: boolean }) {
    localStorage.clear();
    localStorage.setItem(StorageKeys.userId, 'account-1');
    localStorage.setItem(StorageKeys.profileToken, tokenFor('p-andy'));
    localStorage.setItem(StorageKeys.isProfileAdmin, String(profileAdmin));
    localStorage.setItem(StorageKeys.isServerAdmin, String(serverAdmin));
}

async function connect() {
    render(<PlexImportPanel />);
    fireEvent.click(await screen.findByRole('button', { name: 'Connect Plex' }));
    fireEvent.click(screen.getByRole('button', { name: 'Approve on plex.tv' }));
    await screen.findByText('Movies');
}

const lastRequest = () => mocks.runImport.mock.calls.at(-1)?.[1];

describe('PlexImportPanel', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getProviders.mockResolvedValue([{ id: 'plex_library_sync', name: 'Plex Library Sync', providerName: 'Plex', description: '' }]);
        mocks.listServers.mockResolvedValue([{ clientIdentifier: 'srv', name: 'Home', isOwned: true, isOnline: true, connections: [] }]);
        mocks.listLibraries.mockResolvedValue([{ key: '1', name: 'Movies', kind: 'Movie' }]);
        mocks.listAccounts.mockResolvedValue([
            { id: 'andy-uuid', displayName: 'Andy', kind: 'Owner', hasPin: false, isSignedIn: true },
            { id: 'kid-uuid', displayName: 'Mia', kind: 'Home', hasPin: true, isSignedIn: false },
        ]);
        mocks.getUserAccount.mockResolvedValue({ profiles: [{ id: 'p-andy', name: 'Andy' }, { id: 'p-kids', name: 'Kids' }] });
        mocks.runImport.mockResolvedValue({ jobId: 'j1', providerId: 'plex_library_sync', serverName: 'Home', state: 'Pending', startedAt: '', users: [] });
    });

    it("imports the signed-in Plex account's history into this profile by default", async () => {
        signIn({ profileAdmin: true });
        await connect();

        fireEvent.click(screen.getByRole('button', { name: 'Import' }));

        await vi.waitFor(() => expect(mocks.runImport).toHaveBeenCalled());
        expect(lastRequest()).toMatchObject({ plexAccountId: null, pin: null, profileId: null, plexUsername: 'andyx' });
    });

    it("lets the account's main profile import a Plex Home user's history into another profile, with that user's PIN", async () => {
        signIn({ profileAdmin: true });
        await connect();

        fireEvent.change(await screen.findByLabelText('Whose Plex history'), { target: { value: 'kid-uuid' } });
        fireEvent.change(screen.getByLabelText('Import into'), { target: { value: 'p-kids' } });
        expect(screen.getByRole('button', { name: 'Import' })).toBeDisabled();
        fireEvent.change(screen.getByLabelText("Mia's Plex PIN"), { target: { value: '1234' } });
        expect(screen.getByText("Mia's history goes into Kids.")).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Import' }));

        await vi.waitFor(() => expect(mocks.runImport).toHaveBeenCalled());
        expect(lastRequest()).toMatchObject({ plexAccountId: 'kid-uuid', plexAccountName: 'Mia', pin: '1234', profileId: 'p-kids' });
    });

    it('offers no profile choice to a profile that is not the main one', async () => {
        signIn({ profileAdmin: false });
        await connect();

        expect(await screen.findByLabelText('Whose Plex history')).toBeInTheDocument();
        expect(screen.queryByLabelText('Import into')).not.toBeInTheDocument();
        expect(mocks.getUserAccount).not.toHaveBeenCalled();
    });

    it("offers the server admin rating only for the admin's own history into their own profile", async () => {
        signIn({ profileAdmin: true, serverAdmin: true });
        await connect();

        expect(await screen.findByLabelText('Also set these as the server Admin Rating')).toBeInTheDocument();
        fireEvent.change(screen.getByLabelText('Import into'), { target: { value: 'p-kids' } });
        expect(screen.queryByLabelText('Also set these as the server Admin Rating')).not.toBeInTheDocument();
    });

    it("shows why an import failed, such as a wrong PIN", async () => {
        signIn({ profileAdmin: true });
        mocks.runImport.mockResolvedValue({
            jobId: 'j1', providerId: 'plex_library_sync', serverName: 'Home', state: 'Completed', startedAt: '',
            users: [{ accountId: 'kid-uuid', accountName: 'Mia', profileId: 'p-kids', profileName: 'Kids', state: 'Failed', watchStatesFetched: 0, watchStatesImported: 0, watchStatesMerged: 0, ratingsFetched: 0, ratingsImported: 0, ratingsMerged: 0, skipped: 0, skippedSamples: [], errorMessage: "Plex wouldn't switch to that Plex Home user: the PIN is wrong or missing." }],
        });
        await connect();

        fireEvent.click(screen.getByRole('button', { name: 'Import' }));

        expect(await screen.findByText("Plex wouldn't switch to that Plex Home user: the PIN is wrong or missing.")).toBeInTheDocument();
        expect(screen.getByText('Mia → Kids')).toBeInTheDocument();
    });
});

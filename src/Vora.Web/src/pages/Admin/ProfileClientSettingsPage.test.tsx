import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import ProfileClientSettingsPage from './ProfileClientSettingsPage';
import { DialogProvider } from '../../dialogs';
import type { ClientSettingsVM } from '../../api/Users/profileService';
import type { UserVM } from '../../api/Users/userService';

const mocks = vi.hoisted(() => ({
    adminGetClientSettings: vi.fn(),
    adminSaveClientSettings: vi.fn(),
    adminGetDeviceSettings: vi.fn(),
    adminSaveDeviceSettings: vi.fn(),
    getAllUsers: vi.fn(),
}));

vi.mock('../../api/Users/profileService', () => ({
    profileService: {
        adminGetClientSettings: mocks.adminGetClientSettings,
        adminSaveClientSettings: mocks.adminSaveClientSettings,
        adminGetDeviceSettings: mocks.adminGetDeviceSettings,
        adminSaveDeviceSettings: mocks.adminSaveDeviceSettings,
    },
}));
vi.mock('../../api/Users/userService', () => ({ userService: { getAllUsers: mocks.getAllUsers } }));

const settings: ClientSettingsVM = {
    playback: { autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, upNextOrder: 'InOrder', episodeLayout: 'Row', minimumCreditsSceneSeconds: 15 },
    tracks: { audioLanguage: 'fra', subtitleLanguage: null, subtitleMode: null, preferHearingImpairedSubtitles: false, serverLanguage: 'eng', serverSubtitleMode: 'Default' },
    librarySort: { sort: null, descending: false },
};

const users = [{ id: 'u-parent', displayName: 'The Parents', profiles: [{ id: 'p-sam', name: 'Sam' }] }] as unknown as UserVM[];

const tv = {
    deviceId: 'tv', name: 'Living room', nickname: 'Big screen', clientName: 'Vora Android', deviceType: 'TV', operatingSystem: 'Android', lastConnectedAt: '2026-11-24T10:00:00Z',
    settings: { bitrate: 0, maxResolution: 0, seekStepSeconds: 10, volumeStepPercent: 5, soundOnClick: true, soundOnNavOpen: true, soundOnMove: true },
};

const Where = () => <span data-testid="where">{useLocation().pathname}{useLocation().search}</span>;

const renderPage = (entry = '/admin/server/s1/users/p-sam/client-settings') => render(
    <DialogProvider>
        <MemoryRouter initialEntries={[entry]}>
            <Routes>
                <Route path="/admin/server/:serverId/users/:profileId/client-settings" element={<><ProfileClientSettingsPage /><Where /></>} />
                <Route path="/admin/server/:serverId/users" element={<span>Users page</span>} />
            </Routes>
        </MemoryRouter>
    </DialogProvider>,
);

describe('ProfileClientSettingsPage', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.adminGetClientSettings.mockResolvedValue(settings);
        mocks.adminGetDeviceSettings.mockResolvedValue([tv]);
        mocks.getAllUsers.mockResolvedValue(users);
    });

    it("opens on the profile's own settings, named for the profile and its account", async () => {
        renderPage();

        expect(await screen.findByRole('heading', { name: 'Client settings for Sam' })).toBeInTheDocument();
        expect(screen.getByText(/A profile of The Parents/)).toBeInTheDocument();
        expect(screen.getByLabelText('Audio language')).toHaveValue('fra');
        expect(mocks.adminGetClientSettings).toHaveBeenCalledWith('p-sam', 's1');
        expect(mocks.adminGetDeviceSettings).not.toHaveBeenCalled();
    });

    it('does not say a profile belongs to an account that has its name', async () => {
        mocks.getAllUsers.mockResolvedValue([{ id: 'u-sam', displayName: 'Sam', profiles: [{ id: 'p-sam', name: 'Sam' }] }]);
        renderPage();

        await screen.findByRole('heading', { name: 'Client settings for Sam' });

        expect(screen.queryByText(/A profile of/)).toBeNull();
    });

    it('saves through the admin route and reports it, staying on the page', async () => {
        mocks.adminSaveClientSettings.mockResolvedValue({ ...settings, playback: { ...settings.playback, autoSkipCredits: true } });
        renderPage();

        fireEvent.click(await screen.findByRole('checkbox', { name: /Auto-skip end credits/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(mocks.adminSaveClientSettings).toHaveBeenCalledWith('p-sam', expect.objectContaining({
            playback: expect.objectContaining({ autoSkipCredits: true }),
            tracks: expect.objectContaining({ audioLanguage: 'fra' }),
        }), 's1'));
        expect(await screen.findByRole('status')).toHaveTextContent('Saved the client settings for Sam.');
        expect(screen.getByRole('heading', { name: 'Client settings for Sam' })).toBeInTheDocument();
    });

    it("lists the profile's devices on the Devices tab, by nickname", async () => {
        renderPage();

        fireEvent.click(await screen.findByRole('button', { name: 'Devices' }));

        expect(await screen.findByText('Big screen')).toBeInTheDocument();
        expect(mocks.adminGetDeviceSettings).toHaveBeenCalledWith('p-sam', 's1');
        expect(screen.queryByLabelText('Audio language')).toBeNull();
        expect(screen.getByRole('link', { name: 'Authorized Devices' })).toHaveAttribute('href', '/admin/server/s1/devices');
        expect(screen.getByTestId('where')).toHaveTextContent('?tab=devices');
    });

    it('opens on the Devices tab with one device ready to edit when it is sent there', async () => {
        renderPage('/admin/server/s1/users/p-sam/client-settings?tab=devices&device=tv');

        expect(await screen.findByText('Settings for Big screen')).toBeInTheDocument();
        expect(screen.queryByLabelText('Audio language')).toBeNull();
    });

    it('goes back to the profile tab without keeping the device it was sent to', async () => {
        renderPage('/admin/server/s1/users/p-sam/client-settings?tab=devices&device=tv');
        await screen.findByText('Settings for Big screen');

        fireEvent.click(screen.getByRole('button', { name: 'Profile' }));
        expect(await screen.findByLabelText('Audio language')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Devices' }));

        expect(await screen.findByText('Big screen')).toBeInTheDocument();
        expect(screen.queryByText('Settings for Big screen')).toBeNull();
    });

    it('says when the profile cannot be read, and offers no form', async () => {
        mocks.adminGetClientSettings.mockRejectedValue(new AxiosError('Request failed', 'ERR_BAD_REQUEST', undefined, undefined, { status: 404, statusText: 'Not Found', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Not found', detail: 'Profile not found.' } }));
        renderPage();

        expect(await screen.findByRole('alert')).toHaveTextContent('Profile not found.');
        expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Devices' })).toBeNull();
    });

    it('says when the profile is not any account’s any more', async () => {
        mocks.getAllUsers.mockResolvedValue([{ id: 'u-other', displayName: 'Other', profiles: [] }]);
        renderPage();

        expect(await screen.findByRole('alert')).toHaveTextContent('This profile no longer exists.');
    });

    it('links back to Users & Access', async () => {
        renderPage();

        expect(await screen.findByRole('link', { name: 'Back to Users & Access' })).toHaveAttribute('href', '/admin/server/s1/users');
    });
});

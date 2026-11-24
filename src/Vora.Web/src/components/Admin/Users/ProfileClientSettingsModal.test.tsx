import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import ProfileClientSettingsModal from './ProfileClientSettingsModal';
import { DialogProvider } from '../../../dialogs';
import type { ClientSettingsVM, UserProfileVM } from '../../../api/Users/profileService';

const mocks = vi.hoisted(() => ({
    adminGetClientSettings: vi.fn(),
    adminSaveClientSettings: vi.fn(),
    adminGetDeviceSettings: vi.fn(),
    adminSaveDeviceSettings: vi.fn(),
}));

vi.mock('../../../api/Users/profileService', () => ({
    profileService: {
        adminGetClientSettings: mocks.adminGetClientSettings,
        adminSaveClientSettings: mocks.adminSaveClientSettings,
        adminGetDeviceSettings: mocks.adminGetDeviceSettings,
        adminSaveDeviceSettings: mocks.adminSaveDeviceSettings,
    },
}));

const sam = { id: 'p-sam', name: 'Sam' } as UserProfileVM;

const settings: ClientSettingsVM = {
    playback: { autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, upNextOrder: 'InOrder', episodeLayout: 'Row', minimumCreditsSceneSeconds: 15 },
    tracks: { audioLanguage: 'fra', subtitleLanguage: null, subtitleMode: null, preferHearingImpairedSubtitles: false, serverLanguage: 'eng', serverSubtitleMode: 'Default' },
    librarySort: { sort: null, descending: false },
};

const renderModal = (onSaved = vi.fn(), onClose = vi.fn()) =>
    render(<DialogProvider><ProfileClientSettingsModal profile={sam} serverId="s1" onClose={onClose} onSaved={onSaved} /></DialogProvider>);

describe('ProfileClientSettingsModal', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.adminGetClientSettings.mockResolvedValue(settings);
        mocks.adminGetDeviceSettings.mockResolvedValue([]);
    });

    it("opens on the profile's own settings, named for the profile", async () => {
        renderModal();

        expect(await screen.findByRole('heading', { name: 'Client settings for Sam' })).toBeInTheDocument();
        expect(screen.getByLabelText('Audio language')).toHaveValue('fra');
        expect(mocks.adminGetClientSettings).toHaveBeenCalledWith('p-sam', 's1');
    });

    it('saves through the admin route and reports it', async () => {
        const saved = { ...settings, playback: { ...settings.playback, autoSkipCredits: true } };
        mocks.adminSaveClientSettings.mockResolvedValue(saved);
        const onSaved = vi.fn();
        renderModal(onSaved);

        fireEvent.click(await screen.findByRole('checkbox', { name: /Auto-skip end credits/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(onSaved).toHaveBeenCalledWith('Saved the client settings for Sam.'));
        expect(mocks.adminSaveClientSettings).toHaveBeenCalledWith('p-sam', expect.objectContaining({
            playback: expect.objectContaining({ autoSkipCredits: true }),
            tracks: expect.objectContaining({ audioLanguage: 'fra' }),
        }), 's1');
    });

    it('says when the profile cannot be read, and offers no form', async () => {
        mocks.adminGetClientSettings.mockRejectedValue(new AxiosError('Request failed', 'ERR_BAD_REQUEST', undefined, undefined, { status: 404, statusText: 'Not Found', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Not found', detail: 'Profile not found.' } }));
        renderModal();

        expect(await screen.findByRole('alert')).toHaveTextContent('Profile not found.');
        expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
    });

    it("lists the profile's devices below its settings", async () => {
        mocks.adminGetDeviceSettings.mockResolvedValue([
            { deviceId: 'tv', name: 'Living room', clientName: 'Vora Android', deviceType: 'TV', operatingSystem: 'Android', settings: { bitrate: 0, maxResolution: 0, seekStepSeconds: 10, volumeStepPercent: 5, soundOnClick: true, soundOnNavOpen: true, soundOnMove: true } },
        ]);
        renderModal();

        expect(await screen.findByText('Living room')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Devices' })).toBeInTheDocument();
        expect(mocks.adminGetDeviceSettings).toHaveBeenCalledWith('p-sam', 's1');
    });

    it('closes without saving', async () => {
        const onClose = vi.fn();
        renderModal(vi.fn(), onClose);

        await screen.findByRole('heading', { name: 'Client settings for Sam' });
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(onClose).toHaveBeenCalled();
        expect(mocks.adminSaveClientSettings).not.toHaveBeenCalled();
    });
});

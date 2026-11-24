import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ClientDefaultsPage from './ClientDefaultsPage';
import { DialogProvider } from '../../dialogs';
import type { ClientSettingsVM } from '../../api/Users/profileService';

const mocks = vi.hoisted(() => ({
    get: vi.fn(),
    save: vi.fn(),
}));

vi.mock('../../api/System/clientDefaultsService', () => ({
    clientDefaultsService: { get: mocks.get, save: mocks.save },
}));

const defaults: ClientSettingsVM = {
    playback: { autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, upNextOrder: 'WhereILeftOff', episodeLayout: 'Row', minimumCreditsSceneSeconds: 15 },
    tracks: { audioLanguage: null, subtitleLanguage: null, subtitleMode: null, preferHearingImpairedSubtitles: false, serverLanguage: 'eng', serverSubtitleMode: 'Smart' },
    librarySort: { sort: null, descending: false },
};

const renderPage = () => render(
    <DialogProvider>
        <MemoryRouter initialEntries={['/admin/server/s1/client-defaults']}>
            <Routes>
                <Route path="/admin/server/:serverId/client-defaults" element={<ClientDefaultsPage />} />
            </Routes>
        </MemoryRouter>
    </DialogProvider>,
);

describe('ClientDefaultsPage', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.get.mockResolvedValue(defaults);
    });

    it('says what the defaults are for, and shows the current ones', async () => {
        renderPage();

        expect(await screen.findByLabelText('Audio language')).toHaveValue('');
        expect(screen.getByRole('heading', { name: 'Client Defaults' })).toBeInTheDocument();
        expect(screen.getByText(/changing these never touches a profile that already exists/)).toBeInTheDocument();
        expect(mocks.get).toHaveBeenCalledWith('s1');
    });

    it('saves a new default for the server and confirms it', async () => {
        mocks.save.mockResolvedValue({ ...defaults, tracks: { ...defaults.tracks, audioLanguage: 'spa' } });
        renderPage();

        fireEvent.change(await screen.findByLabelText('Audio language'), { target: { value: 'spa' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save defaults' }));

        await waitFor(() => expect(mocks.save).toHaveBeenCalledWith(expect.objectContaining({
            tracks: expect.objectContaining({ audioLanguage: 'spa' }),
        }), 's1'));
        expect(await screen.findByRole('status')).toHaveTextContent('Saved the client defaults.');
    });

    it('says when the defaults cannot be read', async () => {
        mocks.get.mockRejectedValue(new AxiosError('Request failed', 'ERR_BAD_REQUEST', undefined, undefined, { status: 404, statusText: 'Not Found', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Not found', detail: 'Only an admin can read the defaults.' } }));
        renderPage();

        expect(await screen.findByRole('alert')).toHaveTextContent('Only an admin can read the defaults.');
        expect(screen.queryByRole('button', { name: 'Save defaults' })).toBeNull();
    });
});

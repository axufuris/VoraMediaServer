import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import AiPlaylistsSettings from './AiPlaylistsSettings';
import SaveMixButton from '../../Collections/SaveMixButton';
import type { ServerSettings } from '../../../api/System/systemSettingsAdminService';

const mocks = vi.hoisted(() => ({
    getFeatureFlags: vi.fn(),
    saveMixAsPlaylist: vi.fn(),
}));

vi.mock('../../../api/System/featureFlagsService', () => ({ featureFlagsService: { getFeatureFlags: mocks.getFeatureFlags } }));
vi.mock('../../../api/Music/musicService', () => ({ musicService: { saveMixAsPlaylist: mocks.saveMixAsPlaylist } }));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const settings = (overrides: Partial<ServerSettings> = {}): ServerSettings => ({
    enableAiMusicPlaylists: false, enableAiPlaylistRequests: true, aiPlaylistRequestsPerDay: 10, ...overrides,
} as ServerSettings);

describe('AiPlaylistsSettings', () => {
    beforeEach(() => Object.values(mocks).forEach(m => m.mockReset()));

    it('starts off, with the request settings unavailable until it is on', async () => {
        mocks.getFeatureFlags.mockResolvedValue({ aiPlaylists: false });
        const onChange = vi.fn();
        render(<AiPlaylistsSettings serverSettings={settings()} savedSettings={settings()} onChange={onChange} />);

        expect(screen.getByRole('checkbox', { name: 'AI playlists' })).not.toBeChecked();
        expect(screen.getByRole('checkbox', { name: /Make me a playlist/ })).toBeDisabled();

        fireEvent.click(screen.getByRole('checkbox', { name: 'AI playlists' }));
        expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ enableAiMusicPlaylists: true }));
    });

    // The toggle alone isn't enough: it also needs For You and an OpenAI key.
    it("says so when it is switched on but can't run", async () => {
        mocks.getFeatureFlags.mockResolvedValue({ aiPlaylists: false });
        render(<AiPlaylistsSettings serverSettings={settings({ enableAiMusicPlaylists: true })} savedSettings={settings({ enableAiMusicPlaylists: true })} onChange={vi.fn()} />);

        expect(await screen.findByRole('status')).toHaveTextContent(/needs For You switched on and an OpenAI API key/);
    });

    it('says nothing more once it is running', async () => {
        mocks.getFeatureFlags.mockResolvedValue({ aiPlaylists: true });
        render(<AiPlaylistsSettings serverSettings={settings({ enableAiMusicPlaylists: true })} savedSettings={settings({ enableAiMusicPlaylists: true })} onChange={vi.fn()} />);

        await screen.findByRole('checkbox', { name: /Make me a playlist/ });
        expect(screen.queryByRole('status')).toBeNull();
    });

    // The bug: the status was checked once, when the page opened, so after
    // saving the warning stayed until a refresh.
    it('checks again after a save, so a fixed setup stops warning without a refresh', async () => {
        mocks.getFeatureFlags.mockResolvedValueOnce({ aiPlaylists: false }).mockResolvedValue({ aiPlaylists: true });
        const on = settings({ enableAiMusicPlaylists: true });
        const { rerender } = render(<AiPlaylistsSettings serverSettings={on} savedSettings={on} onChange={vi.fn()} />);
        expect(await screen.findByRole('status')).toBeInTheDocument();

        const savedAgain = { ...on };
        rerender(<AiPlaylistsSettings serverSettings={savedAgain} savedSettings={savedAgain} onChange={vi.fn()} />);

        await waitFor(() => expect(screen.queryByRole('status')).toBeNull());
        expect(screen.getByRole('button', { name: 'Make AI playlists now' })).toBeInTheDocument();
        expect(mocks.getFeatureFlags).toHaveBeenCalledTimes(2);
    });

    // Ticking the box changes nothing on the server until Save, so it says so
    // instead of warning about a setting that isn't saved yet.
    it('asks for a save rather than warning while the change is unsaved', async () => {
        mocks.getFeatureFlags.mockResolvedValue({ aiPlaylists: false });
        render(<AiPlaylistsSettings serverSettings={settings({ enableAiMusicPlaylists: true })} savedSettings={settings()} onChange={vi.fn()} />);

        expect(await screen.findByText('Save to turn AI playlists on.')).toBeInTheDocument();
        expect(screen.queryByRole('status')).toBeNull();
    });
});

describe('SaveMixButton', () => {
    beforeEach(() => Object.values(mocks).forEach(m => m.mockReset()));

    it('saves the mix and opens the new playlist', async () => {
        mocks.saveMixAsPlaylist.mockResolvedValue({ id: 'p1' });
        render(
            <MemoryRouter initialEntries={['/music']}>
                <Routes>
                    <Route path="/music" element={<SaveMixButton mixId="m1" />} />
                    <Route path="/playlist/:id" element={<div>playlist page</div>} />
                </Routes>
            </MemoryRouter>,
        );

        fireEvent.click(screen.getByRole('button', { name: 'Save to my playlists' }));

        expect(await screen.findByText('playlist page')).toBeInTheDocument();
        expect(mocks.saveMixAsPlaylist).toHaveBeenCalledWith('m1', undefined);
    });
});

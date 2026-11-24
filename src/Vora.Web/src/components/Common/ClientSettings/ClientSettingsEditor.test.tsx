import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import ClientSettingsEditor from './ClientSettingsEditor';
import { DialogProvider } from '../../../dialogs';
import type { ClientSettingsVM, SaveClientSettingsRequest } from '../../../api/Users/profileService';

const settings: ClientSettingsVM = {
    playback: { autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, upNextOrder: 'WhereILeftOff', episodeLayout: 'Row', minimumCreditsSceneSeconds: 15 },
    tracks: { audioLanguage: null, subtitleLanguage: null, subtitleMode: null, preferHearingImpairedSubtitles: false, serverLanguage: 'deu', serverSubtitleMode: 'Smart' },
    librarySort: { sort: null, descending: false },
};

const renderEditor = (onSave: (request: SaveClientSettingsRequest) => Promise<ClientSettingsVM>, onSaved = vi.fn()) =>
    render(<DialogProvider><ClientSettingsEditor settings={settings} onSave={onSave} onSaved={onSaved} saveLabel="Save defaults" /></DialogProvider>);

describe('ClientSettingsEditor', () => {
    it('shows every group, with the server language and mode beside the automatic choices', () => {
        renderEditor(vi.fn());

        expect(screen.getByRole('heading', { name: 'Playback' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Audio and subtitles' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Library sort order' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Automatic (Smart, set by the server)' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Automatic (German (Deutsch))' })).toBeInTheDocument();
        expect(screen.getByRole('checkbox', { name: /Play the next episode automatically/ })).toBeChecked();
    });

    it('can only be saved once something has changed', () => {
        renderEditor(vi.fn());

        expect(screen.getByRole('button', { name: 'Save defaults' })).toBeDisabled();
        fireEvent.click(screen.getByRole('checkbox', { name: /Auto-skip intros/ }));
        expect(screen.getByRole('button', { name: 'Save defaults' })).toBeEnabled();
    });

    it('sends every group, and shows what the server stored', async () => {
        const saved: ClientSettingsVM = {
            ...settings,
            playback: { ...settings.playback, autoSkipIntro: true },
            tracks: { ...settings.tracks, audioLanguage: 'spa' },
            librarySort: { sort: 'DateAdded', descending: true },
        };
        const onSave = vi.fn().mockResolvedValue(saved);
        const onSaved = vi.fn();
        renderEditor(onSave, onSaved);

        fireEvent.click(screen.getByRole('checkbox', { name: /Auto-skip intros/ }));
        fireEvent.change(screen.getByLabelText('Audio language'), { target: { value: 'spa' } });
        fireEvent.change(screen.getByLabelText('Sort by'), { target: { value: 'dateAdded' } });
        fireEvent.change(screen.getByLabelText('Order'), { target: { value: 'desc' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save defaults' }));

        await waitFor(() => expect(onSave).toHaveBeenCalledWith({
            playback: { autoSkipIntro: true, autoSkipCredits: false, autoPlayNext: true, upNextOrder: 'WhereILeftOff', episodeLayout: 'Row', minimumCreditsSceneSeconds: 15 },
            tracks: { audioLanguage: 'spa', subtitleLanguage: null, subtitleMode: null, preferHearingImpairedSubtitles: false },
            librarySort: { sort: 'DateAdded', descending: true },
        }));
        await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved));
        expect(screen.getByRole('button', { name: 'Save defaults' })).toBeDisabled();
    });

    it('says so when the save fails, and keeps what was picked', async () => {
        const onSave = vi.fn().mockRejectedValue(new Error('boom'));
        const onSaved = vi.fn();
        renderEditor(onSave, onSaved);

        fireEvent.click(screen.getByRole('checkbox', { name: /Auto-skip intros/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Save defaults' }));

        expect(await screen.findByText('Could not save the settings: boom')).toBeInTheDocument();
        expect(onSaved).not.toHaveBeenCalled();
        expect(screen.getByRole('checkbox', { name: /Auto-skip intros/ })).toBeChecked();
    });
});

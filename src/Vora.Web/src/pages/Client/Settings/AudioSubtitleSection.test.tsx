import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import AudioSubtitleSection from './AudioSubtitleSection';
import { DialogProvider } from '../../../dialogs';
import { profileService, type TrackPreferencesVM } from '../../../api/Users/profileService';

vi.mock('../../../api/Users/profileService', () => ({
    profileService: {
        getMyTrackPreferences: vi.fn(),
        saveMyTrackPreferences: vi.fn(),
    },
}));

const automatic: TrackPreferencesVM = {
    audioLanguage: null,
    subtitleLanguage: null,
    subtitleMode: null,
    preferHearingImpairedSubtitles: false,
    serverLanguage: 'deu',
    serverSubtitleMode: 'Smart',
};

const renderSection = () => render(<DialogProvider><AudioSubtitleSection serverId="server" /></DialogProvider>);

beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(profileService.getMyTrackPreferences).mockResolvedValue(automatic);
    vi.mocked(profileService.saveMyTrackPreferences).mockImplementation(async request => ({ ...automatic, ...request }));
});

it('names what Automatic means on this server instead of leaving the choice blank', async () => {
    renderSection();

    expect(await screen.findByRole('option', { name: 'Automatic (Smart, set by the server)' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Automatic (German (Deutsch))' })).toBeInTheDocument();
    expect(screen.getByLabelText('Audio language')).toHaveValue('');
    expect(screen.getByText("Also turns subtitles on when the audio isn't in your subtitle language.")).toBeInTheDocument();
});

it('saves the picks to the profile and Automatic as nothing chosen', async () => {
    renderSection();
    const save = await screen.findByRole('button', { name: 'Save audio & subtitles' });
    expect(save).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Audio language'), { target: { value: 'jpn' } });
    fireEvent.change(screen.getByLabelText('Subtitles'), { target: { value: 'Always' } });
    fireEvent.click(screen.getByRole('checkbox', { name: /deaf and hard of hearing/ }));
    expect(screen.getByText('Always shows full subtitles in your subtitle language.')).toBeInTheDocument();
    fireEvent.click(save);

    await waitFor(() => expect(profileService.saveMyTrackPreferences).toHaveBeenCalledWith({
        audioLanguage: 'jpn',
        subtitleLanguage: null,
        subtitleMode: 'Always',
        preferHearingImpairedSubtitles: true,
    }, 'server'));
    await waitFor(() => expect(save).toBeDisabled());
});

it('keeps a saved language that is not in the list', async () => {
    vi.mocked(profileService.getMyTrackPreferences).mockResolvedValue({ ...automatic, audioLanguage: 'yue' });
    renderSection();

    expect(await screen.findByRole('option', { name: 'yue' })).toBeInTheDocument();
    expect(screen.getByLabelText('Audio language')).toHaveValue('yue');
});

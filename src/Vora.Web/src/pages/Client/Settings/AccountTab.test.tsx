import { expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import AccountTab from './AccountTab';
import { DialogProvider } from '../../../dialogs';

const profile = vi.hoisted(() => ({
    getMyShowtimesLocation: vi.fn(),
    getMyPlaybackPreferences: vi.fn(),
    saveMyPlaybackPreferences: vi.fn(),
    getMyLibrarySort: vi.fn(),
}));

vi.mock('../../../api/Users/profileService', () => ({ profileService: profile }));

beforeEach(() => {
    profile.getMyShowtimesLocation.mockResolvedValue({ location: null });
    profile.getMyPlaybackPreferences.mockResolvedValue({ autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, minimumCreditsSceneSeconds: 15 });
    profile.saveMyPlaybackPreferences.mockImplementation((prefs: object) => Promise.resolve(prefs));
    profile.getMyLibrarySort.mockResolvedValue({ sort: null, descending: false });
});

it('leaves account, play history and switching profile to the profile menu', async () => {
    render(<DialogProvider><AccountTab serverId="home" /></DialogProvider>);

    expect(await screen.findByText('Movie showtimes location')).toBeInTheDocument();
    expect(screen.queryByText('Manage your profile')).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
});

it('plays the next episode automatically until the profile turns it off', async () => {
    render(<DialogProvider><AccountTab serverId="home" /></DialogProvider>);

    const autoPlay = await screen.findByRole('checkbox', { name: /Play the next episode automatically/ });
    expect(autoPlay).toBeChecked();

    fireEvent.click(autoPlay);
    const playback = autoPlay.closest('section');
    expect(playback).not.toBeNull();
    const saveButtons = screen.getAllByRole('button', { name: 'Save' }).filter(button => playback?.contains(button));
    fireEvent.click(saveButtons[0]);

    await waitFor(() => expect(profile.saveMyPlaybackPreferences).toHaveBeenCalledWith(expect.objectContaining({ autoPlayNext: false, autoSkipIntro: false }), 'home'));
    expect(autoPlay).not.toBeChecked();
});

it('treats a server that does not know the setting yet as playing on', async () => {
    profile.getMyPlaybackPreferences.mockResolvedValue({ autoSkipIntro: true, autoSkipCredits: false, minimumCreditsSceneSeconds: 15 });

    render(<DialogProvider><AccountTab serverId="home" /></DialogProvider>);

    expect(await screen.findByRole('checkbox', { name: /Play the next episode automatically/ })).toBeChecked();
});

it('starts a show at the next unwatched episode and shows episodes as a row until the profile picks otherwise', async () => {
    profile.getMyPlaybackPreferences.mockResolvedValue({ autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, minimumCreditsSceneSeconds: 15 });
    render(<DialogProvider><AccountTab serverId="home" /></DialogProvider>);

    const order = await screen.findByLabelText('Play on a show or season starts at');
    const layout = screen.getByLabelText("Show a season's episodes as");
    expect(order).toHaveValue('InOrder');
    expect(layout).toHaveValue('Row');

    fireEvent.change(order, { target: { value: 'WhereILeftOff' } });
    fireEvent.change(layout, { target: { value: 'List' } });
    expect(screen.getByText(/Starts at the episode you played most recently/)).toBeInTheDocument();
    const playback = order.closest('section');
    fireEvent.click(screen.getAllByRole('button', { name: 'Save' }).filter(button => playback?.contains(button))[0]);

    await waitFor(() => expect(profile.saveMyPlaybackPreferences).toHaveBeenCalledWith(
        expect.objectContaining({ upNextOrder: 'WhereILeftOff', episodeLayout: 'List', autoPlayNext: true }), 'home'));
});

it('shows the saved choices of a profile that picked where it left off and a list', async () => {
    profile.getMyPlaybackPreferences.mockResolvedValue({ autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, upNextOrder: 'WhereILeftOff', episodeLayout: 'List', minimumCreditsSceneSeconds: 15 });
    render(<DialogProvider><AccountTab serverId="home" /></DialogProvider>);

    expect(await screen.findByLabelText('Play on a show or season starts at')).toHaveValue('WhereILeftOff');
    expect(screen.getByLabelText("Show a season's episodes as")).toHaveValue('List');
});

import { expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import AccountTab from './AccountTab';
import { DialogProvider } from '../../../dialogs';

vi.mock('../../../api/Users/profileService', () => ({
    profileService: {
        getMyShowtimesLocation: vi.fn().mockResolvedValue({ location: null }),
        getMyPlaybackPreferences: vi.fn().mockResolvedValue({ autoSkipIntro: false, autoSkipCredits: false, minimumCreditsSceneSeconds: 15 }),
        getMyLibrarySort: vi.fn().mockResolvedValue({ sort: null, descending: false }),
    },
}));

it('leaves account, play history and switching profile to the profile menu', async () => {
    render(<DialogProvider><AccountTab serverId="home" /></DialogProvider>);

    expect(await screen.findByText('Movie showtimes location')).toBeInTheDocument();
    expect(screen.queryByText('Manage your profile')).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
});

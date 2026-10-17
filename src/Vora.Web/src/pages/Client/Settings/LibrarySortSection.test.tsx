import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import LibrarySortSection from './LibrarySortSection';
import { DialogProvider } from '../../../dialogs';
import { profileService, type LibrarySortVM } from '../../../api/Users/profileService';

vi.mock('../../../api/Users/profileService', () => ({
    profileService: {
        getMyLibrarySort: vi.fn(),
        saveMyLibrarySort: vi.fn(),
    },
}));

const renderSection = () => render(<DialogProvider><LibrarySortSection serverId="home" /></DialogProvider>);

beforeEach(() => {
    vi.mocked(profileService.getMyLibrarySort).mockResolvedValue({ sort: null, descending: false });
    vi.mocked(profileService.saveMyLibrarySort).mockImplementation(async (sort: LibrarySortVM) => sort);
});

it('starts on the server default, with no order to pick', async () => {
    renderSection();

    const sortBy = await screen.findByLabelText('Sort by');
    expect(sortBy).toHaveValue('server');
    expect(screen.getByLabelText('Order')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save sort order' })).toBeDisabled();
});

it('saves release date, newest first, to the profile', async () => {
    renderSection();

    fireEvent.change(await screen.findByLabelText('Sort by'), { target: { value: 'releaseDate' } });
    fireEvent.change(screen.getByLabelText('Order'), { target: { value: 'desc' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save sort order' }));

    await waitFor(() => expect(profileService.saveMyLibrarySort).toHaveBeenCalledWith({ sort: 'ReleaseDate', descending: true }, 'home'));
    expect(screen.getByRole('button', { name: 'Save sort order' })).toBeDisabled();
});

it('goes back to the server default without keeping a direction', async () => {
    vi.mocked(profileService.getMyLibrarySort).mockResolvedValue({ sort: 'DateAdded', descending: true });
    renderSection();

    const sortBy = await screen.findByLabelText('Sort by');
    expect(sortBy).toHaveValue('dateAdded');
    expect(screen.getByLabelText('Order')).toHaveValue('desc');

    fireEvent.change(sortBy, { target: { value: 'server' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save sort order' }));

    await waitFor(() => expect(profileService.saveMyLibrarySort).toHaveBeenCalledWith({ sort: null, descending: false }, 'home'));
});

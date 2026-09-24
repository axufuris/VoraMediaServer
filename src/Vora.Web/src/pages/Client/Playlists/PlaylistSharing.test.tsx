import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PlaylistSharingControls, PlaylistUnavailable, SavedCopyBanner } from './PlaylistSharing';

describe('PlaylistSharingControls', () => {
    it('gives the owner a Share toggle and nothing to copy', async () => {
        const onToggle = vi.fn(() => Promise.resolve());
        render(<PlaylistSharingControls isOwner isShared={false} ownerName="Sam" onToggleShared={onToggle} onSaveCopy={vi.fn()} />);

        const share = screen.getByRole('button', { name: 'Share' });
        expect(share).toHaveAttribute('aria-pressed', 'false');
        expect(screen.queryByRole('button', { name: 'Save a copy' })).toBeNull();

        fireEvent.click(share);
        await waitFor(() => expect(onToggle).toHaveBeenCalledWith(true));
    });

    it('shows an owner their playlist is shared, and unshares on the next click', async () => {
        const onToggle = vi.fn(() => Promise.resolve());
        render(<PlaylistSharingControls isOwner isShared ownerName="Sam" onToggleShared={onToggle} onSaveCopy={vi.fn()} />);

        const shared = screen.getByRole('button', { name: 'Shared' });
        expect(shared).toHaveAttribute('aria-pressed', 'true');

        fireEvent.click(shared);
        await waitFor(() => expect(onToggle).toHaveBeenCalledWith(false));
    });

    // Someone else's playlist: who it belongs to, and a way to make it yours —
    // but no way to change theirs.
    it('gives a viewer the owner and a copy, never a toggle', async () => {
        const onCopy = vi.fn(() => Promise.resolve());
        render(<PlaylistSharingControls isOwner={false} isShared ownerName="Andy" onToggleShared={vi.fn()} onSaveCopy={onCopy} />);

        expect(screen.getByText('Andy')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /share/i })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Save a copy' }));
        await waitFor(() => expect(onCopy).toHaveBeenCalledOnce());
    });

    // A second click while the first is in flight would make two copies.
    it('does not save two copies from a double click', async () => {
        let resolve: () => void = () => {};
        const onCopy = vi.fn(() => new Promise<void>(r => { resolve = r; }));
        render(<PlaylistSharingControls isOwner={false} isShared ownerName="Andy" onToggleShared={vi.fn()} onSaveCopy={onCopy} />);

        const button = screen.getByRole('button', { name: 'Save a copy' });
        fireEvent.click(button);
        fireEvent.click(button);
        resolve();

        await waitFor(() => expect(onCopy).toHaveBeenCalledOnce());
    });
});

describe('PlaylistUnavailable', () => {
    it('says the playlist is gone and why, without claiming which', () => {
        render(<MemoryRouter><PlaylistUnavailable backTo="/playlists" /></MemoryRouter>);

        expect(screen.getByText("This playlist isn't available anymore")).toBeInTheDocument();
        expect(screen.getByText('Its owner deleted it or stopped sharing it.')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Back to playlists' })).toHaveAttribute('href', '/playlists');
    });
});

describe('SavedCopyBanner', () => {
    it('confirms a copy that was just saved', () => {
        render(
            <MemoryRouter initialEntries={[{ pathname: '/playlist/new', state: { savedCopy: true } }]}>
                <SavedCopyBanner />
            </MemoryRouter>,
        );

        expect(screen.getByRole('status')).toHaveTextContent('Saved to your playlists');
    });

    it('says nothing on an ordinary visit', () => {
        render(<MemoryRouter initialEntries={['/playlist/old']}><SavedCopyBanner /></MemoryRouter>);

        expect(screen.queryByRole('status')).toBeNull();
    });
});

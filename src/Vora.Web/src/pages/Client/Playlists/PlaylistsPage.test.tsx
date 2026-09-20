import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PlaylistsPage from './PlaylistsPage';
import type { PlaylistSummaryVM } from '../../../api/Collections/playlistService';
import type { SmartPlaylistSummaryVM } from '../../../api/Music/smartPlaylistService';
import type { GeneratedMixSummaryVM } from '../../../api/Music/musicService';

const playlists: PlaylistSummaryVM[] = [
    { id: 'p-music', name: 'Road Trip', mediaType: 'Music', itemCount: 12, posterUrls: [], backdropUrls: [] },
    { id: 'p-movies', name: 'Comfort Movies', mediaType: 'Movies', itemCount: 4, posterUrls: [], backdropUrls: [] },
    { id: 'p-mixed', name: 'Party Night', mediaType: 'Mixed', itemCount: 9, posterUrls: [], backdropUrls: [] },
];

const smart: SmartPlaylistSummaryVM[] = [
    { id: 's-music', name: 'Heavy Rotation', mediaType: 'Music', trackCount: 40, createdAt: '2026-09-01', updatedAt: '2026-09-01' },
    { id: 's-shows', name: 'Unwatched Comedy', mediaType: 'Shows', trackCount: 8, createdAt: '2026-09-01', updatedAt: '2026-09-01' },
];

const mixes: GeneratedMixSummaryVM[] = [
    { id: 'm1', slot: 1, name: 'Sunday Chill', trackCount: 50, kind: 'DailyMix', generatedAt: '2026-09-15' },
];

vi.mock('../../../api/Collections/playlistService', () => ({
    playlistService: { getPlaylists: () => Promise.resolve(playlists) },
}));
vi.mock('../../../api/Music/smartPlaylistService', () => ({
    smartPlaylistService: { list: () => Promise.resolve(smart) },
}));
vi.mock('../../../api/Music/musicService', () => ({
    musicService: { getMixes: () => Promise.resolve(mixes) },
}));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

describe('PlaylistsPage', () => {
    beforeEach(() => localStorage.clear());

    it('locked to music shows only music playlists, with no type tabs or mixes', async () => {
        render(
            <MemoryRouter>
                <PlaylistsPage embedded lockedType="music" showMixes={false} />
            </MemoryRouter>,
        );

        expect(await screen.findByText('Road Trip')).toBeInTheDocument();
        expect(screen.getByText('Heavy Rotation')).toBeInTheDocument();
        expect(screen.queryByText('Comfort Movies')).toBeNull();
        expect(screen.queryByText('Unwatched Comedy')).toBeNull();
        expect(screen.queryByText('Sunday Chill')).toBeNull();
        expect(screen.queryByRole('tablist')).toBeNull();
    });

    it('opens on Movies & Shows, which also carries mixed-type playlists', async () => {
        render(
            <MemoryRouter>
                <PlaylistsPage />
            </MemoryRouter>,
        );

        expect(await screen.findByText('Comfort Movies')).toBeInTheDocument();
        expect(screen.getByText('Unwatched Comedy')).toBeInTheDocument();
        expect(screen.getByText('Party Night')).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: 'Movies & Shows' })).toHaveAttribute('aria-selected', 'true');
        expect(screen.queryByRole('tab', { name: 'All' })).toBeNull();
        expect(screen.queryByText('Road Trip')).toBeNull();
        expect(screen.queryByText('Sunday Chill')).toBeNull();
    });

    it('shows music playlists and mixes on the Music tab, and remembers it', async () => {
        const { unmount } = render(
            <MemoryRouter>
                <PlaylistsPage />
            </MemoryRouter>,
        );
        await screen.findByText('Comfort Movies');

        fireEvent.click(screen.getByRole('tab', { name: 'Music' }));

        expect(screen.getByText('Road Trip')).toBeInTheDocument();
        expect(screen.getByText('Sunday Chill')).toBeInTheDocument();
        expect(screen.queryByText('Comfort Movies')).toBeNull();
        unmount();

        render(
            <MemoryRouter>
                <PlaylistsPage />
            </MemoryRouter>,
        );
        expect(await screen.findByRole('tab', { name: 'Music' })).toHaveAttribute('aria-selected', 'true');
    });
});

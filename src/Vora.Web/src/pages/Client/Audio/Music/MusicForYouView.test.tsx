import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import MusicForYouView from './MusicForYouView';
import type { AlbumVM, ArtistTrackVM, RecentlyPlayedItemVM } from '../../../../api/Music/musicService';
import type { AiPlaylistsVM } from '../../../../api/Music/aiPlaylistService';
import type { DialogApi } from '../../../../dialogs/useDialog';

const song = (id: string, title: string, globalListeners?: number | null): ArtistTrackVM => ({
    id,
    title,
    artist: 'Black Eyed Peas',
    trackNumber: 1,
    albumTitle: 'Elephunk',
    isLiked: false,
    globalListeners,
});

const played = (track: ArtistTrackVM): RecentlyPlayedItemVM => ({
    kind: 'Track',
    id: track.id,
    title: track.title,
    subtitle: track.albumTitle,
    artworkUrls: [],
    trackCount: 0,
    playedAt: '2026-11-09T20:00:00Z',
    track,
});

const tile = (kind: RecentlyPlayedItemVM['kind'], id: string, title: string, subtitle: string): RecentlyPlayedItemVM => ({
    kind, id, title, subtitle, artworkUrls: [], trackCount: 20, playedAt: '2026-11-09T20:00:00Z',
});

const ai: AiPlaylistsVM = {
    enabled: true,
    requestsEnabled: true,
    weekly: [{ id: 'w1', kind: 'AiPlaylist', name: 'Late Night Drive', artworkUrls: [], trackCount: 30, generatedAt: '2026-11-09T00:00:00Z' }],
    blends: [],
    requests: [],
};

const album: AlbumVM = { id: 'al1', title: 'Elephunk', isCompilation: false, artistId: 'a1', artistName: 'Black Eyed Peas', lockedFields: [] };

const dialog: DialogApi = { alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) };

const renderForYou = (overrides: Partial<Parameters<typeof MusicForYouView>[0]> = {}) => render(
    <MusicForYouView
        isLoading={false}
        serverPlayback={[]}
        dailyMixes={[]}
        stations={[]}
        becauseYouPlayed={[]}
        recentlyPlayed={[]}
        recentlyAddedAlbums={[album]}
        topArtists={[]}
        topTracks={[]}
        likedCount={0}
        availableYears={[]}
        hasAnyHistory={false}
        aiPlaylists={null}
        updateNav={vi.fn()}
        openPlaylist={vi.fn()}
        playArtistTrackList={vi.fn()}
        startStationRadio={vi.fn()}
        deleteStation={vi.fn()}
        dialog={dialog}
        {...overrides}
    />,
);

describe('MusicForYouView song rows', () => {
    it('show how many Last.fm listeners a song has, in Because you played and Recently Played', () => {
        renderForYou({
            becauseYouPlayed: [{ heading: 'Because you played Black Eyed Peas', seedArtistId: 'a1', tracks: [song('t1', 'Pump It', 900_000)] }],
            recentlyPlayed: [played(song('t2', 'Hey Mama', 1_200_000))],
        });

        expect(screen.getByTitle('900,000 listeners on Last.fm')).toBeInTheDocument();
        expect(screen.getByTitle('1,200,000 listeners on Last.fm')).toBeInTheDocument();
    });

    it('show nothing extra for a song Last.fm has no figure for', () => {
        renderForYou({ recentlyPlayed: [played(song('t3', 'Unknown B-side', null))] });

        expect(screen.getByText('Unknown B-side')).toBeInTheDocument();
        expect(screen.queryByTitle(/listeners on Last\.fm/)).not.toBeInTheDocument();
    });
});

describe('MusicForYouView Recently Played', () => {
    const rowTitles = () => screen.getAllByRole('heading').map(h => h.textContent);

    it('is the first row, above Made for you by AI', () => {
        renderForYou({ aiPlaylists: ai, recentlyPlayed: [played(song('t1', 'Hey Mama'))] });

        const titles = rowTitles();
        expect(titles.indexOf('Recently Played')).toBeLessThan(titles.indexOf('Made for you by AI'));
        expect(titles.indexOf('Made for you by AI')).toBeLessThan(titles.indexOf('Recently Added'));
        expect(titles.filter(t => t === 'Recently Played')).toHaveLength(1);
    });

    it('is not shown before anything has been played', () => {
        renderForYou({ aiPlaylists: ai, recentlyPlayed: [] });

        expect(screen.queryByText('Recently Played')).not.toBeInTheDocument();
    });

    it('shows a playlist or mix once, as itself, and opens it', () => {
        const updateNav = vi.fn();
        const openPlaylist = vi.fn();
        renderForYou({
            updateNav,
            openPlaylist,
            recentlyPlayed: [
                tile('Mix', 'm1', 'Late Night Drive', 'Made for you by AI'),
                tile('Mix', 'm2', 'Rainy Sunday', 'Your request'),
                tile('Playlist', 'p1', 'Gym', 'Playlist'),
                tile('SmartPlaylist', 's1', 'Unplayed Jazz', 'Smart playlist'),
            ],
        });

        expect(screen.getByText('Your request')).toBeInTheDocument();
        fireEvent.click(screen.getByText('Late Night Drive'));
        expect(updateNav).toHaveBeenCalledWith({ view: 'mix', mixId: 'm1' });
        fireEvent.click(screen.getByText('Gym'));
        expect(openPlaylist).toHaveBeenCalledWith('Playlist', 'p1');
        fireEvent.click(screen.getByText('Unplayed Jazz'));
        expect(openPlaylist).toHaveBeenCalledWith('SmartPlaylist', 's1');
    });

    it('plays a song with the row\'s other songs, skipping the playlist tiles', () => {
        const playArtistTrackList = vi.fn();
        const first = song('t1', 'Hey Mama');
        const second = song('t2', 'Pump It');
        renderForYou({
            playArtistTrackList,
            recentlyPlayed: [played(first), tile('Playlist', 'p1', 'Gym', 'Playlist'), played(second)],
        });

        fireEvent.click(screen.getByText('Pump It'));

        expect(playArtistTrackList).toHaveBeenCalledWith([first, second], 1);
    });

    it('shows a song with its album', () => {
        renderForYou({ recentlyPlayed: [played(song('t1', 'Hey Mama'))] });

        const card = screen.getByText('Hey Mama').closest('button') ?? screen.getByText('Hey Mama').parentElement!;
        expect(within(card.parentElement!).getByText(/Elephunk/)).toBeInTheDocument();
    });

    it('leaves out a kind it does not know', () => {
        renderForYou({
            recentlyPlayed: [{ ...tile('Mix', 'x1', 'Something New', 'New'), kind: 'Podcast' as RecentlyPlayedItemVM['kind'] }],
        });

        expect(screen.queryByText('Something New')).not.toBeInTheDocument();
        expect(screen.queryByText('Recently Played')).not.toBeInTheDocument();
    });
});

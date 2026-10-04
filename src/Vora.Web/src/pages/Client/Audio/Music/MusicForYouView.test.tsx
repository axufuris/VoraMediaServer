import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import MusicForYouView from './MusicForYouView';
import type { AlbumVM, ArtistTrackVM } from '../../../../api/Music/musicService';
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
            recentlyPlayed: [song('t2', 'Hey Mama', 1_200_000)],
        });

        expect(screen.getByTitle('900,000 listeners on Last.fm')).toBeInTheDocument();
        expect(screen.getByTitle('1,200,000 listeners on Last.fm')).toBeInTheDocument();
    });

    it('show nothing extra for a song Last.fm has no figure for', () => {
        renderForYou({ recentlyPlayed: [song('t3', 'Unknown B-side', null)] });

        expect(screen.getByText('Unknown B-side')).toBeInTheDocument();
        expect(screen.queryByTitle(/listeners on Last\.fm/)).not.toBeInTheDocument();
    });
});

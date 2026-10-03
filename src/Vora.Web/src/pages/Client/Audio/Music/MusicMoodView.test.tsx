import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import MusicMoodView from './MusicMoodView';
import BrowseByMoodRow from './BrowseByMoodRow';
import type { ArtistTrackVM, MoodTracksVM } from '../../../../api/Music/musicService';

const mocks = vi.hoisted(() => ({
    getMoodTracks: vi.fn(),
    getMoodShuffle: vi.fn(),
}));

vi.mock('../../../../api/Music/musicService', () => ({
    musicService: { getMoodTracks: mocks.getMoodTracks, getMoodShuffle: mocks.getMoodShuffle },
}));
vi.mock('../../../../components/Collections/AddToPlaylistButton', () => ({ default: () => null }));

const song = (id: string, title: string): ArtistTrackVM => ({
    id,
    title,
    trackNumber: 1,
    albumTitle: `${title} album`,
    albumArtworkUrl: `/${id}.jpg`,
    isLiked: false,
});

const page = (tracks: ArtistTrackVM[], totalCount = tracks.length): MoodTracksVM => ({ mood: 'chill', name: 'Chill', totalCount, tracks });

const renderMood = (overrides: { isShuffled?: boolean } = {}) => {
    const props = {
        playArtistTrackList: vi.fn(),
        toggleShuffle: vi.fn(),
        onMissing: vi.fn(),
    };
    render(
        <MusicMoodView
            mood="chill"
            refreshKey={0}
            isShuffled={overrides.isShuffled ?? false}
            formatDuration={() => '3:00'}
            {...props}
        />,
    );
    return props;
};

describe('MusicMoodView', () => {
    beforeEach(() => Object.values(mocks).forEach(m => m.mockReset()));

    it('lists the mood with how many songs have it, most listened first', async () => {
        mocks.getMoodTracks.mockResolvedValue(page([song('t1', 'Teardrop'), song('t2', 'Roads')], 1234));
        renderMood();

        expect(await screen.findByRole('heading', { name: 'Chill' })).toBeInTheDocument();
        expect(screen.getByText('1,234 songs, most listened first')).toBeInTheDocument();
        expect(screen.getByText('Teardrop')).toBeInTheDocument();
        expect(mocks.getMoodTracks).toHaveBeenCalledWith('chill', 0, 100, undefined);
    });

    it('plays the loaded songs from the top, with shuffle off', async () => {
        const tracks = [song('t1', 'Teardrop'), song('t2', 'Roads')];
        mocks.getMoodTracks.mockResolvedValue(page(tracks));
        const { playArtistTrackList, toggleShuffle } = renderMood({ isShuffled: true });

        fireEvent.click(await screen.findByRole('button', { name: 'Play' }));

        expect(toggleShuffle).toHaveBeenCalledTimes(1);
        expect(playArtistTrackList).toHaveBeenCalledWith(tracks, 0);
    });

    it('shuffles from every song with the mood, not just the ones shown', async () => {
        mocks.getMoodTracks.mockResolvedValue(page([song('t1', 'Teardrop')], 500));
        const shuffled = [song('t9', 'Glory Box'), song('t7', 'Unfinished Sympathy')];
        mocks.getMoodShuffle.mockResolvedValue(shuffled);
        const { playArtistTrackList, toggleShuffle } = renderMood();

        fireEvent.click(await screen.findByRole('button', { name: 'Shuffle' }));

        await waitFor(() => expect(playArtistTrackList).toHaveBeenCalledWith(shuffled, 0));
        expect(mocks.getMoodShuffle).toHaveBeenCalledWith('chill', 100, undefined);
        expect(toggleShuffle).toHaveBeenCalledTimes(1);
    });

    it('shows more songs on request until every one is listed', async () => {
        mocks.getMoodTracks
            .mockResolvedValueOnce(page([song('t1', 'Teardrop')], 2))
            .mockResolvedValueOnce(page([song('t2', 'Roads')], 2));
        renderMood();

        fireEvent.click(await screen.findByRole('button', { name: 'Show more (1 left)' }));

        expect(await screen.findByText('Roads')).toBeInTheDocument();
        expect(mocks.getMoodTracks).toHaveBeenLastCalledWith('chill', 1, 100, undefined);
        expect(screen.queryByRole('button', { name: /Show more/ })).not.toBeInTheDocument();
    });

    it('goes back when the server does not know the mood', async () => {
        mocks.getMoodTracks.mockResolvedValue(null);
        const { onMissing } = renderMood();

        await waitFor(() => expect(onMissing).toHaveBeenCalled());
    });
});

describe('BrowseByMoodRow', () => {
    it('opens a mood from its tile and shows how many songs it has', () => {
        const onOpen = vi.fn();
        render(<BrowseByMoodRow moods={[{ mood: 'chill', name: 'Chill', trackCount: 1234, sampleArtworkUrl: '/c.jpg' }]} onOpen={onOpen} />);

        expect(screen.getByText('1,234 songs')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: /Chill/ }));

        expect(onOpen).toHaveBeenCalledWith('chill');
    });

    it('is not shown when no song has been described yet', () => {
        const { container } = render(<BrowseByMoodRow moods={[]} onOpen={vi.fn()} />);

        expect(container).toBeEmptyDOMElement();
    });
});

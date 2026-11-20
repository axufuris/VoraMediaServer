import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SmartListRow from './SmartListRow';
import type { SmartListClientDto, SmartListEntry } from '../../api/Collections/smartListService';
import type { IptvChannelVM } from '../../api/Iptv/iptvAdminService';

const mocks = vi.hoisted(() => ({ getListEntries: vi.fn(), playMedia: vi.fn(), resumeMusic: vi.fn() }));

vi.mock('../../api/Collections/smartListService', () => ({ smartListService: { getListEntries: mocks.getListEntries } }));
vi.mock('../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia: mocks.playMedia }) }));
vi.mock('../../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));
vi.mock('../../hooks/useResumeMusic', () => ({ useResumeMusic: () => mocks.resumeMusic }));
vi.mock('../../dialogs', () => ({ useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true) }) }));

class NoopResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
}

const row = (source: SmartListClientDto['source'], title = 'Row'): SmartListClientDto => ({ id: 'list-1', title, displayOrder: 0, source });

const channel: IptvChannelVM = {
    id: 'ch-1', playlistId: 'p1', externalChannelId: 'news', name: 'News 4', logoUrl: 'https://logos.test/news.png',
    groupTitle: 'Local', streamUrl: 'https://streams.test/news.m3u8', isHiddenByAdmin: false, kind: 'Tv',
} as IptvChannelVM;

const renderRow = (list: SmartListClientDto) => render(
    <MemoryRouter initialEntries={['/']}>
        <Routes>
            <Route path="/" element={<SmartListRow list={list} />} />
            <Route path="/media/:id" element={<p>Details page</p>} />
            <Route path="/music" element={<p>Music page</p>} />
        </Routes>
    </MemoryRouter>
);

describe('SmartListRow', () => {
    beforeEach(() => {
        mocks.getListEntries.mockReset();
        mocks.playMedia.mockReset();
        mocks.resumeMusic.mockReset();
        vi.stubGlobal('ResizeObserver', NoopResizeObserver);
    });

    it('draws nothing when the row has nothing for this profile', async () => {
        mocks.getListEntries.mockResolvedValue([]);

        const { container } = renderRow(row('FavoriteChannels', 'Favorite Channels'));

        await waitFor(() => expect(mocks.getListEntries).toHaveBeenCalled());
        await waitFor(() => expect(container).toBeEmptyDOMElement());
        expect(screen.queryByText('Favorite Channels')).not.toBeInTheDocument();
    });

    it('plays a favorite channel straight from the row', async () => {
        const entry: SmartListEntry = { kind: 'Channel', id: 'ch-1', title: 'News 4', subtitle: 'Evening News', imageUrl: channel.logoUrl, channel };
        mocks.getListEntries.mockResolvedValue([entry]);

        renderRow(row('FavoriteChannels', 'Favorite Channels'));

        expect(await screen.findByText('Favorite Channels')).toBeInTheDocument();
        expect(screen.getByText('Evening News')).toBeInTheDocument();
        expect(screen.getByAltText('News 4')).toHaveAttribute('src', 'https://logos.test/news.png');
        fireEvent.click(screen.getByRole('button', { name: /News 4/ }));

        expect(mocks.playMedia).toHaveBeenCalledWith(expect.objectContaining({
            id: 'ch-1', streamUrl: 'https://streams.test/news.m3u8', subtitle: 'Evening News', container: 'hls', playbackContextType: 'LiveTv',
        }));
    });

    describe('Continue Listening', () => {
        const resume = {
            kind: 'Playlist', id: 'p1', title: 'Road trip', subtitle: 'Karma Police', artworkUrl: 'https://art.test/cover.jpg', artworkUrls: [], trackCount: 24,
            playedAt: '2026-11-19T20:00:00Z', source: 'Playlist', sourceId: 'p1', trackIndex: 2,
        } as const;
        const entry: SmartListEntry = { kind: 'MusicResume', id: 'p1', title: 'Road trip', subtitle: 'Karma Police', imageUrl: resume.artworkUrl, musicResume: resume };

        it('shows each queue with the song it stopped on', async () => {
            mocks.getListEntries.mockResolvedValue([entry]);

            renderRow(row('ContinueListening', 'Continue Listening'));

            expect(await screen.findByText('Continue Listening')).toBeInTheDocument();
            expect(screen.getByText('Karma Police')).toBeInTheDocument();
        });

        it('starts the saved queue when a tile is picked', async () => {
            mocks.getListEntries.mockResolvedValue([entry]);
            mocks.resumeMusic.mockResolvedValue(true);

            renderRow(row('ContinueListening', 'Continue Listening'));
            fireEvent.click(await screen.findByRole('button', { name: /Road trip/ }));

            await waitFor(() => expect(mocks.resumeMusic).toHaveBeenCalledWith(resume));
            expect(mocks.getListEntries).toHaveBeenCalledTimes(1);
        });

        it('loads the row again when there was nothing left to play', async () => {
            mocks.getListEntries.mockResolvedValueOnce([entry]).mockResolvedValueOnce([]);
            mocks.resumeMusic.mockResolvedValue(false);

            const { container } = renderRow(row('ContinueListening', 'Continue Listening'));
            fireEvent.click(await screen.findByRole('button', { name: /Road trip/ }));

            await waitFor(() => expect(mocks.getListEntries).toHaveBeenCalledTimes(2));
            await waitFor(() => expect(container).toBeEmptyDOMElement());
        });
    });

    it('a station plays as radio', async () => {
        const station = { ...channel, id: 'st-1', name: 'Jazz FM', kind: 'Radio' } as IptvChannelVM;
        mocks.getListEntries.mockResolvedValue([{ kind: 'Station', id: 'st-1', title: 'Jazz FM', channel: station } satisfies SmartListEntry]);

        renderRow(row('FavoriteStations'));
        fireEvent.click(await screen.findByRole('button', { name: /Jazz FM/ }));

        expect(mocks.playMedia).toHaveBeenCalledWith(expect.objectContaining({ id: 'st-1', playbackContextType: 'LiveRadio' }));
    });

    it('resumes a podcast episode where the profile left it', async () => {
        mocks.getListEntries.mockResolvedValue([{
            kind: 'PodcastEpisode', id: 'ep-1', title: 'Episode 12', subtitle: 'The Show',
            podcastEpisode: {
                id: 'ep-1', showId: 's1', subscriptionId: 'sub', showTitle: 'The Show', title: 'Episode 12',
                audioUrl: 'https://feeds.test/12.mp3', positionSeconds: 300, durationSeconds: 3600, isPlayed: false,
            },
        } as SmartListEntry]);

        renderRow(row('NewPodcastEpisodes'));
        fireEvent.click(await screen.findByRole('button', { name: /Episode 12/ }));

        expect(mocks.playMedia).toHaveBeenCalledWith(expect.objectContaining({ streamUrl: 'https://feeds.test/12.mp3', startPosition: 300, playbackContextType: 'Podcast' }));
    });

    it('a library title opens its details page', async () => {
        mocks.getListEntries.mockResolvedValue([{
            kind: 'Media', id: 'm1', title: 'Heat',
            media: { id: 'm1', title: 'Heat', type: 'Movie', libraryId: 'lib' },
        } satisfies SmartListEntry]);

        renderRow(row('Library'));
        fireEvent.click(await screen.findByRole('button', { name: /Heat/ }));

        expect(await screen.findByText('Details page')).toBeInTheDocument();
    });

    it('a show with episodes left shows how many', async () => {
        mocks.getListEntries.mockResolvedValue([{
            kind: 'Media', id: 's1', title: 'Agents of S.H.I.E.L.D.',
            media: { id: 's1', title: 'Agents of S.H.I.E.L.D.', type: 'TvShow', libraryId: 'lib', unplayedItemCount: 7 },
        } satisfies SmartListEntry]);

        renderRow(row('Library'));

        expect(await screen.findByRole('button', { name: /Agents of S\.H\.I\.E\.L\.D\./ })).toBeInTheDocument();
        expect(screen.getByText('7')).toBeInTheDocument();
    });

    it('an album opens the Music page on that album', async () => {
        mocks.getListEntries.mockResolvedValue([{
            kind: 'Album', id: 'a1', title: 'Elephunk',
            album: { id: 'a1', title: 'Elephunk', artistId: 'ar1', artistName: 'The Black Eyed Peas', isCompilation: false, lockedFields: [] },
        } as SmartListEntry]);

        renderRow(row('RecentlyAddedMusic'));
        fireEvent.click(await screen.findByRole('button', { name: /Elephunk/ }));

        expect(await screen.findByText('Music page')).toBeInTheDocument();
        expect(JSON.parse(sessionStorage.getItem('music_nav_state') ?? '{}')).toEqual({ view: 'album', artistId: 'ar1', albumId: 'a1' });
    });
});

describe('SmartListRow and the sidebar', () => {
    beforeEach(() => {
        mocks.getListEntries.mockReset();
        vi.stubGlobal('ResizeObserver', NoopResizeObserver);
    });

    const movie = (id: string, title: string): SmartListEntry =>
        ({ kind: 'Media', id, title, media: { id, title, type: 'Movie', libraryId: 'lib' } });

    it('asks for the row without the libraries the profile unpinned', async () => {
        mocks.getListEntries.mockResolvedValue([movie('m1', 'Heat')]);

        render(
            <MemoryRouter>
                <SmartListRow list={row('Library', 'Recently Added Movies & Shows')} serverId="home" excludeLibraryIds={['test-movies', 'test-shows']} />
            </MemoryRouter>
        );

        expect(await screen.findByText('Recently Added Movies & Shows')).toBeInTheDocument();
        expect(mocks.getListEntries).toHaveBeenCalledWith('list-1', 'home', ['test-movies', 'test-shows']);
    });

    it('waits while the libraries to leave out are not known yet, showing a library row as loading', async () => {
        mocks.getListEntries.mockResolvedValue([movie('m1', 'Heat')]);
        const view = render(
            <MemoryRouter>
                <SmartListRow list={row('Library', 'Recently Added')} excludeLibraryIds={null} />
            </MemoryRouter>
        );
        await Promise.resolve();

        expect(mocks.getListEntries).not.toHaveBeenCalled();
        expect(view.container.querySelector('.vora-skeleton')).not.toBeNull();

        view.rerender(
            <MemoryRouter>
                <SmartListRow list={row('Library', 'Recently Added')} excludeLibraryIds={['test-shows']} />
            </MemoryRouter>
        );

        expect(await screen.findByText('Heat')).toBeInTheDocument();
        expect(mocks.getListEntries).toHaveBeenCalledTimes(1);
        expect(mocks.getListEntries).toHaveBeenCalledWith('list-1', undefined, ['test-shows']);
    });

    it('asks again when a library is pinned or unpinned, keeping the row on screen meanwhile', async () => {
        mocks.getListEntries.mockResolvedValueOnce([movie('s1', 'Seinfeld'), movie('m1', 'Heat')]);
        const view = render(
            <MemoryRouter>
                <SmartListRow list={row('Library', 'Recently Added')} excludeLibraryIds={[]} />
            </MemoryRouter>
        );
        expect(await screen.findByText('Seinfeld')).toBeInTheDocument();

        let answer: (entries: SmartListEntry[]) => void = () => { };
        mocks.getListEntries.mockReturnValueOnce(new Promise<SmartListEntry[]>(resolve => { answer = resolve; }));
        view.rerender(
            <MemoryRouter>
                <SmartListRow list={row('Library', 'Recently Added')} excludeLibraryIds={['test-shows']} />
            </MemoryRouter>
        );

        expect(mocks.getListEntries).toHaveBeenLastCalledWith('list-1', undefined, ['test-shows']);
        expect(screen.getByText('Seinfeld')).toBeInTheDocument();

        answer([movie('m1', 'Heat'), movie('m2', 'Alien')]);

        await waitFor(() => expect(screen.queryByText('Seinfeld')).not.toBeInTheDocument());
        expect(screen.getByText('Alien')).toBeInTheDocument();
    });

    it('keeps the answer to the latest question when an earlier one comes back late', async () => {
        let answerFirst: (entries: SmartListEntry[]) => void = () => { };
        mocks.getListEntries.mockReturnValueOnce(new Promise<SmartListEntry[]>(resolve => { answerFirst = resolve; }));
        const view = render(
            <MemoryRouter>
                <SmartListRow list={row('Library', 'Recently Added')} excludeLibraryIds={[]} />
            </MemoryRouter>
        );

        mocks.getListEntries.mockResolvedValueOnce([movie('m1', 'Heat')]);
        view.rerender(
            <MemoryRouter>
                <SmartListRow list={row('Library', 'Recently Added')} excludeLibraryIds={['test-shows']} />
            </MemoryRouter>
        );
        expect(await screen.findByText('Heat')).toBeInTheDocument();

        await act(async () => answerFirst([movie('s1', 'Seinfeld')]));

        expect(screen.getByText('Heat')).toBeInTheDocument();
        expect(screen.queryByText('Seinfeld')).not.toBeInTheDocument();
    });
});

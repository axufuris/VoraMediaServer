import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SmartListRow from './SmartListRow';
import type { SmartListClientDto, SmartListEntry } from '../../api/Collections/smartListService';
import type { IptvChannelVM } from '../../api/Iptv/iptvAdminService';

const mocks = vi.hoisted(() => ({ getListEntries: vi.fn(), playMedia: vi.fn() }));

vi.mock('../../api/Collections/smartListService', () => ({ smartListService: { getListEntries: mocks.getListEntries } }));
vi.mock('../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia: mocks.playMedia }) }));
vi.mock('../../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));
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

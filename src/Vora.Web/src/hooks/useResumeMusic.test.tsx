import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import type { ArtistTrackVM, MusicResumeItemVM } from '../api/Music/musicService';

const mocks = vi.hoisted(() => ({ getResumeQueue: vi.fn(), playQueue: vi.fn() }));
vi.mock('../api/Music/musicService', () => ({
    musicService: { getResumeQueue: mocks.getResumeQueue, getTrackStreamUrl: (id: string) => `/stream/${id}` },
}));
vi.mock('../contexts/usePlayer', () => ({ usePlayer: () => ({ playQueue: mocks.playQueue }) }));
vi.mock('../utils/serverVault', () => ({ serverVault: { getServer: () => ({ id: 's1', url: 'https://vora.test' }), getActiveServer: () => ({ id: 's1', url: 'https://vora.test' }) } }));

import { useResumeMusic } from './useResumeMusic';

const track = (id: string, title: string): ArtistTrackVM => ({ id, title, artist: 'Artist', trackNumber: 1, albumTitle: 'Album', isLiked: false });

const item = (overrides: Partial<MusicResumeItemVM> = {}): MusicResumeItemVM => ({
    kind: 'Playlist', id: 'p1', title: 'Road trip', artworkUrls: [], trackCount: 3, playedAt: '2026-11-19T20:00:00Z',
    source: 'Playlist', sourceId: 'p1', trackIndex: 1, ...overrides,
});

describe('continuing a queue', () => {
    beforeEach(() => {
        mocks.getResumeQueue.mockReset();
        mocks.playQueue.mockReset();
    });

    it('plays the saved queue from the saved song, tagged with where it came from', async () => {
        mocks.getResumeQueue.mockResolvedValue({ source: 'Playlist', sourceId: 'p1', index: 2, tracks: [track('a', 'One'), track('b', 'Two'), track('c', 'Three')] });
        const { result } = renderHook(() => useResumeMusic('s1'));

        await expect(result.current(item())).resolves.toBe(true);

        expect(mocks.getResumeQueue).toHaveBeenCalledWith('Playlist', 'p1', 's1');
        const [items, index] = mocks.playQueue.mock.calls[0];
        expect(index).toBe(2);
        expect(items.map((i: { id: string }) => i.id)).toEqual(['a', 'b', 'c']);
        expect(items[0]).toMatchObject({ playbackContextType: 'Music', musicSource: { type: 'Playlist', id: 'p1' }, streamUrl: '/stream/a', serverId: 's1' });
    });

    it('plays a queue that came from nowhere in particular without a source', async () => {
        mocks.getResumeQueue.mockResolvedValue({ source: 'None', sourceId: null, index: 0, tracks: [track('a', 'One')] });
        const { result } = renderHook(() => useResumeMusic('s1'));

        await result.current(item({ source: 'None', sourceId: null, kind: 'Track', id: 'a' }));

        expect(mocks.getResumeQueue).toHaveBeenCalledWith('None', undefined, 's1');
        expect(mocks.playQueue.mock.calls[0][0][0].musicSource).toBeUndefined();
    });

    it('keeps an album as the source of the songs it plays', async () => {
        mocks.getResumeQueue.mockResolvedValue({ source: 'Album', sourceId: 'al1', index: 0, tracks: [track('a', 'One')] });
        const { result } = renderHook(() => useResumeMusic('s1'));

        await result.current(item({ source: 'Album', sourceId: 'al1', kind: 'Track', id: 'a' }));

        expect(mocks.playQueue.mock.calls[0][0][0].musicSource).toEqual({ type: 'Album', id: 'al1' });
    });

    it('plays nothing and says so when there is nothing left to play', async () => {
        const { result } = renderHook(() => useResumeMusic('s1'));

        mocks.getResumeQueue.mockResolvedValueOnce(null);
        await expect(result.current(item())).resolves.toBe(false);
        mocks.getResumeQueue.mockResolvedValueOnce({ source: 'Playlist', sourceId: 'p1', index: 0, tracks: [] });
        await expect(result.current(item())).resolves.toBe(false);

        expect(mocks.playQueue).not.toHaveBeenCalled();
    });
});

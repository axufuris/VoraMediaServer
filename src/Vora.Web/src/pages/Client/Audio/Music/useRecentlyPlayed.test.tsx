import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { RecentlyPlayedItemVM } from '../../../../api/Music/musicService';

const handlers = new Map<string, (payload: unknown) => void>();
vi.mock('../../../../hooks/useSignalREvent', () => ({
    useSignalREvent: (event: string, callback: (payload: unknown) => void) => { handlers.set(event, callback); },
}));

const getRecentlyPlayedItems = vi.fn();
vi.mock('../../../../api/Music/musicService', () => ({
    musicService: { getRecentlyPlayedItems: (...args: unknown[]) => getRecentlyPlayedItems(...args) },
}));

import { useRecentlyPlayed, RECENTLY_PLAYED_LIMIT } from './useRecentlyPlayed';

const mix = { kind: 'Mix', id: 'm1', title: 'Ultimate Party Vibes', artworkUrls: [], playedAt: '2026-11-11T00:38:33Z' } as unknown as RecentlyPlayedItemVM;

describe('useRecentlyPlayed', () => {
    beforeEach(() => {
        handlers.clear();
        getRecentlyPlayedItems.mockReset();
    });

    it('starts with what the page loaded and fetches nothing by itself', () => {
        const { result } = renderHook(() => useRecentlyPlayed('s1'));
        act(() => result.current[1]([mix]));

        expect(result.current[0]).toEqual([mix]);
        expect(getRecentlyPlayedItems).not.toHaveBeenCalled();
    });

    it('reloads the row when a play is saved for the profile', async () => {
        getRecentlyPlayedItems.mockResolvedValue([mix]);
        const { result } = renderHook(() => useRecentlyPlayed('s1'));

        act(() => handlers.get('MusicHistoryUpdated')?.('profile-1'));

        await waitFor(() => expect(result.current[0]).toEqual([mix]));
        expect(getRecentlyPlayedItems).toHaveBeenCalledWith(RECENTLY_PLAYED_LIMIT, 's1');
    });

    it('keeps the row it has when the reload fails', async () => {
        const error = vi.spyOn(console, 'error').mockImplementation(() => { });
        getRecentlyPlayedItems.mockRejectedValue(new Error('offline'));
        const { result } = renderHook(() => useRecentlyPlayed('s1'));
        act(() => result.current[1]([mix]));

        act(() => handlers.get('MusicHistoryUpdated')?.('profile-1'));

        await waitFor(() => expect(error).toHaveBeenCalled());
        expect(result.current[0]).toEqual([mix]);
        error.mockRestore();
    });
});

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { MusicResumeItemVM } from '../../../../api/Music/musicService';

const handlers = new Map<string, (payload: unknown) => void>();
vi.mock('../../../../hooks/useSignalREvent', () => ({
    useSignalREvent: (event: string, callback: (payload: unknown) => void) => { handlers.set(event, callback); },
}));

const getResumeItems = vi.fn();
vi.mock('../../../../api/Music/musicService', () => ({
    musicService: { getResumeItems: (...args: unknown[]) => getResumeItems(...args) },
}));

import { useMusicResume, MUSIC_RESUME_LIMIT } from './useMusicResume';

const playlist = { kind: 'Playlist', id: 'p1', title: 'Road trip', artworkUrls: [], source: 'Playlist', sourceId: 'p1', trackIndex: 1 } as unknown as MusicResumeItemVM;

describe('useMusicResume', () => {
    beforeEach(() => {
        handlers.clear();
        getResumeItems.mockReset();
    });

    it('loads the row when the page opens', async () => {
        getResumeItems.mockResolvedValue([playlist]);

        const { result } = renderHook(() => useMusicResume('s1'));

        await waitFor(() => expect(result.current.items).toEqual([playlist]));
        expect(getResumeItems).toHaveBeenCalledWith(MUSIC_RESUME_LIMIT, 's1');
    });

    it('reloads when the server says a queue was saved for the profile', async () => {
        getResumeItems.mockResolvedValueOnce([]).mockResolvedValueOnce([playlist]);
        const { result } = renderHook(() => useMusicResume('s1'));
        await waitFor(() => expect(getResumeItems).toHaveBeenCalledTimes(1));

        act(() => handlers.get('MusicResumeUpdated')?.('profile-1'));

        await waitFor(() => expect(result.current.items).toEqual([playlist]));
    });

    it('keeps the row it has when a reload fails', async () => {
        const error = vi.spyOn(console, 'error').mockImplementation(() => { });
        getResumeItems.mockResolvedValueOnce([playlist]).mockRejectedValueOnce(new Error('offline'));
        const { result } = renderHook(() => useMusicResume('s1'));
        await waitFor(() => expect(result.current.items).toEqual([playlist]));

        act(() => result.current.reload());

        await waitFor(() => expect(error).toHaveBeenCalled());
        expect(result.current.items).toEqual([playlist]);
        error.mockRestore();
    });
});

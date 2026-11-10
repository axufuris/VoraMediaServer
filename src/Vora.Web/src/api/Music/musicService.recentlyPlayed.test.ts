import { describe, it, expect, vi, beforeEach } from 'vitest';

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));

vi.mock('../client', () => ({ apiClient: { get: mocks.get, post: mocks.post }, getResponseStatus: vi.fn() }));

import { musicService } from './musicService';

describe('Recently Played', () => {
    beforeEach(() => {
        mocks.get.mockReset();
        mocks.post.mockReset();
    });

    it('asks for the row under the client’s /api base, not a second /api', async () => {
        mocks.get.mockResolvedValue({ data: [] });

        await musicService.getRecentlyPlayedItems(20, 'server-1');

        expect(mocks.get).toHaveBeenCalledWith('/music/history/recently-played', { params: { limit: 20 }, serverId: 'server-1' });
    });

    it('reports a play with what it was played from', async () => {
        mocks.post.mockResolvedValue({ data: { recorded: true } });

        await musicService.recordPlay('t1', 200, true, 'server-1', { type: 'Mix', id: 'm1' });

        expect(mocks.post).toHaveBeenCalledWith(
            '/music/tracks/t1/played',
            { durationListenedSeconds: 200, completed: true, source: 'Mix', sourceId: 'm1' },
            { serverId: 'server-1' });
    });

    it('reports a play from nowhere in particular as before', async () => {
        mocks.post.mockResolvedValue({ data: { recorded: true } });

        await musicService.recordPlay('t1', 40, false);

        expect(mocks.post).toHaveBeenCalledWith('/music/tracks/t1/played', { durationListenedSeconds: 40, completed: false }, { serverId: undefined });
    });
});

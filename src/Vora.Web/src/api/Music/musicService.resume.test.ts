import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), status: vi.fn() }));
vi.mock('../client', () => ({ apiClient: { get: mocks.get, put: mocks.put }, getResponseStatus: mocks.status }));

import { musicService } from './musicService';

describe('Continue Listening calls', () => {
    beforeEach(() => {
        mocks.get.mockReset();
        mocks.put.mockReset();
        mocks.status.mockReset();
    });

    it('asks for the row under the client’s /api base', async () => {
        mocks.get.mockResolvedValue({ data: [] });

        await musicService.getResumeItems(10, 'server-a');

        expect(mocks.get).toHaveBeenCalledWith('/music/resume', { params: { limit: 10 }, serverId: 'server-a' });
    });

    it('asks for the queue to play by source and id', async () => {
        mocks.get.mockResolvedValue({ data: { source: 'Mix', sourceId: 'm1', index: 1, tracks: [] } });

        const queue = await musicService.getResumeQueue('Mix', 'm1', 'server-a');

        expect(queue?.index).toBe(1);
        expect(mocks.get).toHaveBeenCalledWith('/music/resume/queue', { params: { source: 'Mix', sourceId: 'm1' }, serverId: 'server-a' });
    });

    it('is no queue when the server has none left to play', async () => {
        mocks.get.mockRejectedValue(new Error('not found'));
        mocks.status.mockReturnValue(404);

        await expect(musicService.getResumeQueue('None', undefined, 'server-a')).resolves.toBeNull();
    });

    it('sends the save as it is', async () => {
        mocks.put.mockResolvedValue({ data: undefined });
        const save = { source: 'Playlist' as const, sourceId: 'p1', trackIds: ['a', 'b'], index: 1 };

        await musicService.saveResume(save, 'server-b');

        expect(mocks.put).toHaveBeenCalledWith('/music/resume', save, { serverId: 'server-b' });
    });

    it('never fails a save the server could not take', async () => {
        mocks.put.mockRejectedValue(new Error('offline'));
        mocks.status.mockReturnValue(500);

        await expect(musicService.saveResume({ source: 'None', trackIds: ['a'], index: 0 }, 'server-c')).resolves.toBeUndefined();
        await musicService.saveResume({ source: 'None', trackIds: ['a'], index: 0 }, 'server-c');

        expect(mocks.put).toHaveBeenCalledTimes(2);
    });

    it('stops asking a server that has no such endpoint, but not any other server', async () => {
        mocks.status.mockReturnValue(404);
        mocks.get.mockRejectedValueOnce(new Error('not found'));

        await expect(musicService.getResumeItems(10, 'old-server')).resolves.toEqual([]);
        await expect(musicService.getResumeItems(10, 'old-server')).resolves.toEqual([]);
        await musicService.saveResume({ source: 'None', trackIds: ['a'], index: 0 }, 'old-server');

        expect(mocks.get).toHaveBeenCalledTimes(1);
        expect(mocks.put).not.toHaveBeenCalled();

        mocks.get.mockResolvedValueOnce({ data: [{ id: 'p1' }] });
        await expect(musicService.getResumeItems(10, 'new-server')).resolves.toHaveLength(1);
    });

    it('stops saving to a server that answers a save with 405', async () => {
        mocks.status.mockReturnValue(405);
        mocks.put.mockRejectedValueOnce(new Error('method not allowed'));

        await musicService.saveResume({ source: 'None', trackIds: ['a'], index: 0 }, 'gateway');
        await musicService.saveResume({ source: 'None', trackIds: ['a'], index: 0 }, 'gateway');

        expect(mocks.put).toHaveBeenCalledTimes(1);
    });

    it('lets a real failure to load the row through', async () => {
        mocks.get.mockRejectedValue(new Error('offline'));
        mocks.status.mockReturnValue(500);

        await expect(musicService.getResumeItems(10, 'server-d')).rejects.toThrow('offline');
    });
});

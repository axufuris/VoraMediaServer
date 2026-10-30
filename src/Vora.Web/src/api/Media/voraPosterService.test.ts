import { describe, it, expect, vi, beforeEach } from 'vitest';

const mocks = vi.hoisted(() => ({ get: vi.fn() }));

vi.mock('../client', () => ({ apiClient: { get: mocks.get } }));

const catalog = {
    categories: [{ id: 'holidays', name: 'Holidays' }],
    posters: [{ id: 'halloween', category: 'holidays', title: 'Halloween', aliases: ['halloween'], url: '/api/artwork/vora/holidays/halloween.jpg?v=1' }],
};

describe('getVoraPosters', () => {
    beforeEach(() => {
        vi.resetModules();
        mocks.get.mockReset();
    });

    it('asks for the list under the client’s /api base, not a second /api', async () => {
        mocks.get.mockResolvedValue({ data: catalog });
        const { getVoraPosters } = await import('./voraPosterService');

        await expect(getVoraPosters('server-1')).resolves.toEqual(catalog);

        expect(mocks.get).toHaveBeenCalledWith('/artwork/vora', { serverId: 'server-1' });
    });

    it('asks each server once', async () => {
        mocks.get.mockResolvedValue({ data: catalog });
        const { getVoraPosters } = await import('./voraPosterService');

        await getVoraPosters('server-1');
        await getVoraPosters('server-1');
        await getVoraPosters('server-2');

        expect(mocks.get).toHaveBeenCalledTimes(2);
    });

    it('asks again after a failed request', async () => {
        mocks.get.mockRejectedValueOnce(new Error('offline')).mockResolvedValue({ data: catalog });
        const { getVoraPosters } = await import('./voraPosterService');

        await expect(getVoraPosters()).rejects.toThrow('offline');
        await expect(getVoraPosters()).resolves.toEqual(catalog);

        expect(mocks.get).toHaveBeenCalledTimes(2);
    });
});

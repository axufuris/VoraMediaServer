import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { usePendingRequestCount } from './usePendingRequestCount';

const mocks = vi.hoisted(() => ({
    getPendingCount: vi.fn<(serverId?: string) => Promise<number>>(),
    handlers: new Map<string, () => void>(),
}));

vi.mock('../api/Discovery/requestAdminService', () => ({ requestAdminService: { getPendingCount: mocks.getPendingCount } }));
vi.mock('./useSignalREvent', () => ({
    useSignalREvent: (name: string, handler: () => void) => { mocks.handlers.set(name, handler); },
}));

describe('usePendingRequestCount', () => {
    beforeEach(() => {
        mocks.getPendingCount.mockReset().mockResolvedValue(0);
        mocks.handlers.clear();
    });

    it('reads the count when it mounts, for the server it is on', async () => {
        mocks.getPendingCount.mockResolvedValue(9);

        const { result } = renderHook(() => usePendingRequestCount('server-1'));

        await waitFor(() => expect(result.current).toBe(9));
        expect(mocks.getPendingCount).toHaveBeenCalledWith('server-1');
    });

    it('reads it again when the server says the queue changed', async () => {
        mocks.getPendingCount.mockResolvedValueOnce(2);
        const { result } = renderHook(() => usePendingRequestCount(undefined));
        await waitFor(() => expect(result.current).toBe(2));

        mocks.getPendingCount.mockResolvedValueOnce(3);
        await act(async () => { mocks.handlers.get('RequestQueueChanged')?.(); });

        await waitFor(() => expect(result.current).toBe(3));
    });

    it('keeps the last count when a read fails', async () => {
        mocks.getPendingCount.mockResolvedValueOnce(4);
        const { result } = renderHook(() => usePendingRequestCount(undefined));
        await waitFor(() => expect(result.current).toBe(4));

        mocks.getPendingCount.mockRejectedValueOnce(new Error('offline'));
        await act(async () => { mocks.handlers.get('RequestQueueChanged')?.(); });

        expect(result.current).toBe(4);
    });
});

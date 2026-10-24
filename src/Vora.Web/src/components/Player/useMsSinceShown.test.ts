import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useMsSinceShown } from './useMsSinceShown';

describe('how long a prompt has been showing', () => {
    beforeEach(() => { vi.useFakeTimers(); });
    afterEach(() => { vi.useRealTimers(); });

    it('counts from when it appears and stops counting at the limit', () => {
        const { result } = renderHook(() => useMsSinceShown('intro:95.00', 10_000));
        expect(result.current).toBe(0);

        act(() => { vi.advanceTimersByTime(4_000); });
        expect(result.current).toBe(4_000);

        act(() => { vi.advanceTimersByTime(8_000); });
        expect(result.current).toBe(10_000);
        expect(vi.getTimerCount()).toBe(0);
    });

    it('starts again for the next prompt', () => {
        const { result, rerender } = renderHook(({ key }) => useMsSinceShown(key, 10_000), { initialProps: { key: 'intro:95.00' } });
        act(() => { vi.advanceTimersByTime(10_000); });

        rerender({ key: 'credits-to-end' });
        expect(result.current).toBe(0);

        act(() => { vi.advanceTimersByTime(3_000); });
        expect(result.current).toBe(3_000);
    });

    it('starts again when the same prompt comes back after leaving', () => {
        const { result, rerender } = renderHook(({ key }: { key: string | null }) => useMsSinceShown(key, 10_000), { initialProps: { key: 'intro:95.00' } });
        act(() => { vi.advanceTimersByTime(10_000); });

        rerender({ key: null });
        expect(result.current).toBe(0);

        rerender({ key: 'intro:95.00' });
        expect(result.current).toBe(0);
        act(() => { vi.advanceTimersByTime(1_000); });
        expect(result.current).toBe(1_000);
    });
});

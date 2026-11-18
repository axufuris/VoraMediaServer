import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useSidebarPinned } from './useSidebarPinned';
import { StorageKeys } from '../../utils/storageKeys';

describe('useSidebarPinned', () => {
    beforeEach(() => localStorage.clear());

    afterEach(() => vi.restoreAllMocks());

    it('starts unpinned and remembers a pin in this browser', () => {
        const { result } = renderHook(() => useSidebarPinned());
        expect(result.current[0]).toBe(false);

        act(() => result.current[1]());

        expect(result.current[0]).toBe(true);
        expect(localStorage.getItem(StorageKeys.sidebarPinned)).toBe('true');

        act(() => result.current[1]());

        expect(result.current[0]).toBe(false);
        expect(localStorage.getItem(StorageKeys.sidebarPinned)).toBe('false');
    });

    it('opens pinned when that was the last choice', () => {
        localStorage.setItem(StorageKeys.sidebarPinned, 'true');

        expect(renderHook(() => useSidebarPinned()).result.current[0]).toBe(true);
    });

    it('still works when the browser will not store anything', () => {
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
        const { result } = renderHook(() => useSidebarPinned());

        expect(result.current[0]).toBe(false);

        act(() => result.current[1]());

        expect(result.current[0]).toBe(true);
    });
});

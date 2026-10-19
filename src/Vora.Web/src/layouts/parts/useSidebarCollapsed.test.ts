import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useSidebarCollapsed } from './useSidebarCollapsed';
import { StorageKeys } from '../../utils/storageKeys';

describe('useSidebarCollapsed', () => {
    beforeEach(() => localStorage.clear());

    afterEach(() => vi.restoreAllMocks());

    it('starts expanded and remembers a collapse in this browser', () => {
        const { result } = renderHook(() => useSidebarCollapsed());
        expect(result.current[0]).toBe(false);

        act(() => result.current[1]());

        expect(result.current[0]).toBe(true);
        expect(localStorage.getItem(StorageKeys.sidebarCollapsed)).toBe('true');

        act(() => result.current[1]());

        expect(result.current[0]).toBe(false);
        expect(localStorage.getItem(StorageKeys.sidebarCollapsed)).toBe('false');
    });

    it('opens collapsed when that was the last choice', () => {
        localStorage.setItem(StorageKeys.sidebarCollapsed, 'true');

        expect(renderHook(() => useSidebarCollapsed()).result.current[0]).toBe(true);
    });

    it('opens expanded and still toggles when storage is unavailable', () => {
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
        const { result } = renderHook(() => useSidebarCollapsed());

        expect(result.current[0]).toBe(false);
        act(() => result.current[1]());
        expect(result.current[0]).toBe(true);
    });
});

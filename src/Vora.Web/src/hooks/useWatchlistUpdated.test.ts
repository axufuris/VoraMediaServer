import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useWatchlistUpdated } from './useWatchlistUpdated';

const hub = vi.hoisted(() => ({ handlers: new Map<string, (payload: unknown) => void>() }));

vi.mock('./useSignalREvent', () => ({
    useSignalREvent: (name: string, handler: (payload: unknown) => void) => { hub.handlers.set(name, handler); },
}));

vi.mock('../api/client', () => ({
    profileIdForServer: (serverId?: string) => (serverId === 'other' ? '22222222-0000-0000-0000-000000000000' : '8E35A9EC-BB9A-4DFA-B687-00833E862CF9'),
}));

const fire = (payload: unknown) => hub.handlers.get('WatchlistUpdated')?.(payload);

describe('useWatchlistUpdated', () => {
    beforeEach(() => hub.handlers.clear());

    it("calls back for this profile's change, whatever the id's case", () => {
        const onChange = vi.fn();
        renderHook(() => useWatchlistUpdated(undefined, onChange));

        fire('8e35a9ec-bb9a-4dfa-b687-00833e862cf9');

        expect(onChange).toHaveBeenCalledTimes(1);
    });

    it("ignores another profile's change", () => {
        const onChange = vi.fn();
        renderHook(() => useWatchlistUpdated(undefined, onChange));

        fire('11111111-0000-0000-0000-000000000000');
        fire(undefined);

        expect(onChange).not.toHaveBeenCalled();
    });

    it("compares with the profile on the page's own server", () => {
        const onChange = vi.fn();
        renderHook(() => useWatchlistUpdated('other', onChange));

        fire('8e35a9ec-bb9a-4dfa-b687-00833e862cf9');
        expect(onChange).not.toHaveBeenCalled();

        fire('22222222-0000-0000-0000-000000000000');
        expect(onChange).toHaveBeenCalledTimes(1);
    });
});

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, act, cleanup } from '@testing-library/react';
import { StorageKeys } from '../utils/storageKeys';

// Mock @microsoft/signalr BEFORE importing useSignalREvent.
// HubConnectionBuilder.build() returns a mock connection whose .on()/.off()
// calls let us inspect handler registration. .start() resolves immediately.
type Handler = (payload: unknown) => void;

const mockConnection = {
    handlers: new Map<string, Handler[]>(),
    on: vi.fn((event: string, handler: Handler) => {
        const existing = mockConnection.handlers.get(event) ?? [];
        existing.push(handler);
        mockConnection.handlers.set(event, existing);
    }),
    off: vi.fn((event: string, handler: Handler) => {
        const existing = mockConnection.handlers.get(event) ?? [];
        mockConnection.handlers.set(event, existing.filter(h => h !== handler));
    }),
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
    fire: (event: string, payload: unknown) => {
        const handlers = mockConnection.handlers.get(event) ?? [];
        handlers.forEach(h => h(payload));
    },
    reset: () => {
        mockConnection.handlers.clear();
        mockConnection.on.mockClear();
        mockConnection.off.mockClear();
        mockConnection.start.mockClear();
        mockConnection.stop.mockClear();
    }
};

vi.mock('@microsoft/signalr', () => {
    class HubConnectionBuilder {
        withUrl() { return this; }
        withAutomaticReconnect() { return this; }
        build() { return mockConnection; }
    }
    return { HubConnectionBuilder };
});

// Helper to use the hook inside a test component.
async function renderHook(eventName: string, callback: (p: unknown) => void) {
    const { useSignalREvent } = await import('./useSignalREvent');
    function Component() {
        useSignalREvent(eventName, callback);
        return null;
    }
    return render(<Component />);
}

describe('useSignalREvent', () => {
    beforeEach(() => {
        mockConnection.reset();
        localStorage.clear();
    });

    afterEach(() => {
        cleanup();
    });

    it('does not subscribe when no token is present', async () => {
        const callback = vi.fn();
        await renderHook('LogEntryBatch', callback);

        expect(mockConnection.on).not.toHaveBeenCalled();
    });

    it('subscribes to the event when a profile token is present', async () => {
        localStorage.setItem(StorageKeys.profileToken, 'fake-jwt-token');
        const callback = vi.fn();

        await renderHook('LogEntryBatch', callback);

        expect(mockConnection.on).toHaveBeenCalledWith('LogEntryBatch', expect.any(Function));
    });

    it('invokes the latest callback when the event fires', async () => {
        localStorage.setItem(StorageKeys.profileToken, 'fake-jwt-token');
        const callback = vi.fn();

        await renderHook('LibraryUpdated', callback);

        await act(async () => {
            mockConnection.fire('LibraryUpdated', { id: 'abc', name: 'Movies' });
        });

        expect(callback).toHaveBeenCalledWith({ id: 'abc', name: 'Movies' });
    });

    it('unsubscribes from the event on unmount', async () => {
        localStorage.setItem(StorageKeys.profileToken, 'fake-jwt-token');
        const callback = vi.fn();

        const { unmount } = await renderHook('MediaItemUpdated', callback);

        const onCallsBefore = mockConnection.on.mock.calls.length;
        unmount();

        expect(mockConnection.off).toHaveBeenCalledWith('MediaItemUpdated', expect.any(Function));
        // The same handler that was registered should be the one passed to off.
        const registeredHandler = mockConnection.on.mock.calls[onCallsBefore - 1][1];
        const removedHandler = mockConnection.off.mock.calls[mockConnection.off.mock.calls.length - 1][1];
        expect(removedHandler).toBe(registeredHandler);
    });

    it('uses the latest callback after rerender without resubscribing', async () => {
        localStorage.setItem(StorageKeys.profileToken, 'fake-jwt-token');
        const { useSignalREvent } = await import('./useSignalREvent');

        function Component({ cb }: { cb: (p: unknown) => void }) {
            useSignalREvent('LogEntryBatch', cb);
            return null;
        }

        const first = vi.fn();
        const { rerender } = render(<Component cb={first} />);
        const onCallsAfterFirstMount = mockConnection.on.mock.calls.length;

        const second = vi.fn();
        rerender(<Component cb={second} />);

        // No resubscribe should happen because eventName didn't change.
        expect(mockConnection.on.mock.calls.length).toBe(onCallsAfterFirstMount);

        // Firing the event should now hit the latest callback only.
        await act(async () => {
            mockConnection.fire('LogEntryBatch', 'payload');
        });

        expect(first).not.toHaveBeenCalled();
        expect(second).toHaveBeenCalledWith('payload');
    });

    it('resubscribes when the eventName changes', async () => {
        localStorage.setItem(StorageKeys.profileToken, 'fake-jwt-token');
        const { useSignalREvent } = await import('./useSignalREvent');

        function Component({ event }: { event: string }) {
            useSignalREvent(event, () => { });
            return null;
        }

        const { rerender } = render(<Component event="LogEntryBatch" />);
        const onCallsBefore = mockConnection.on.mock.calls.length;

        rerender(<Component event="LibraryUpdated" />);

        expect(mockConnection.off).toHaveBeenCalledWith('LogEntryBatch', expect.any(Function));
        expect(mockConnection.on.mock.calls.length).toBe(onCallsBefore + 1);
        expect(mockConnection.on.mock.calls[onCallsBefore][0]).toBe('LibraryUpdated');
    });
});

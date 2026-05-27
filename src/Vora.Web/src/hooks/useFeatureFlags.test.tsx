import type { ReactNode } from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, cleanup } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { DEFAULT_FEATURE_FLAGS, type FeatureFlagsVM } from '../api/System/featureFlagsService';

// vi.mock is hoisted above local consts, so we use vi.hoisted to declare the
// shared mock fn that both the factory and the test bodies will reference.
const mocks = vi.hoisted(() => ({
    getFeatureFlagsMock: vi.fn(),
}));

vi.mock('../api/System/featureFlagsService', async () => {
    const actual = await vi.importActual<typeof import('../api/System/featureFlagsService')>(
        '../api/System/featureFlagsService'
    );
    return {
        ...actual,
        featureFlagsService: {
            getFeatureFlags: mocks.getFeatureFlagsMock,
            updateFeatureFlags: vi.fn(),
        },
    };
});

vi.mock('../utils/serverVault', () => ({
    serverVault: {
        getActiveServerId: vi.fn(() => null),
    },
}));

// Renders the hook inside a route so useParams resolves.
async function renderHookWith(serverId?: string) {
    const { useFeatureFlags } = await import('./useFeatureFlags');

    const wrapper = serverId
        ? ({ children }: { children: ReactNode }) => (
            <MemoryRouter initialEntries={[`/server/${serverId}`]}>
                <Routes>
                    <Route path="/server/:serverId" element={children} />
                </Routes>
            </MemoryRouter>
        )
        : ({ children }: { children: ReactNode }) => (
            <MemoryRouter>{children}</MemoryRouter>
        );

    const { result } = renderHook(() => useFeatureFlags(), { wrapper });

    return () => result.current;
}

describe('useFeatureFlags', () => {
    beforeEach(() => {
        mocks.getFeatureFlagsMock.mockReset();
    });

    afterEach(() => {
        cleanup();
    });

    it('returns DEFAULT_FEATURE_FLAGS initially before any fetch', async () => {
        mocks.getFeatureFlagsMock.mockResolvedValue({ ...DEFAULT_FEATURE_FLAGS, liveTv: false });
        const get = await renderHookWith('server-a');

        // Synchronous initial value
        expect(get()).toEqual(DEFAULT_FEATURE_FLAGS);
    });

    it('loads flags for the route serverId param', async () => {
        const serverFlags: FeatureFlagsVM = {
            discover: false,
            forYou: false,
            releaseCalendar: true,
            liveTv: true,
            dvr: false,
            internetRadio: true,
            podcasts: false
        };
        mocks.getFeatureFlagsMock.mockResolvedValue(serverFlags);

        const get = await renderHookWith('server-xyz');

        await waitFor(() => {
            expect(get()).toEqual(serverFlags);
        });

        expect(mocks.getFeatureFlagsMock).toHaveBeenCalledWith('server-xyz');
    });

    it('falls back to defaults when no server id is available', async () => {
        const get = await renderHookWith(undefined);

        await waitFor(() => {
            expect(get()).toEqual(DEFAULT_FEATURE_FLAGS);
        });

        expect(mocks.getFeatureFlagsMock).not.toHaveBeenCalled();
    });

    it('falls back to defaults on fetch error', async () => {
        mocks.getFeatureFlagsMock.mockRejectedValue(new Error('boom'));
        // Suppress console.error noise from the catch block.
        const spy = vi.spyOn(console, 'error').mockImplementation(() => { });

        const get = await renderHookWith('server-a');

        await waitFor(() => {
            expect(get()).toEqual(DEFAULT_FEATURE_FLAGS);
        });

        spy.mockRestore();
    });
});

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { featureFlagsService, DEFAULT_FEATURE_FLAGS, type UpdateFeatureFlagsRequest } from './featureFlagsService';

const mocks = vi.hoisted(() => ({ put: vi.fn(), get: vi.fn() }));

vi.mock('../client', () => ({
    apiClient: {
        put: (url: string, body: unknown, config?: unknown) => mocks.put(url, body, config),
        get: (url: string, config?: unknown) => mocks.get(url, config),
    },
}));

const savedBody = (): UpdateFeatureFlagsRequest => mocks.put.mock.calls[0][1] as UpdateFeatureFlagsRequest;

describe('feature flags service', () => {
    beforeEach(() => {
        mocks.put.mockReset();
        mocks.put.mockResolvedValue({ data: undefined });
    });

    // The server reads liveTv and internetRadio from this body straight into the
    // stored toggles. Sending the derived values would erase an admin's choice
    // the moment their IPTV source stopped resolving.
    it('sends the stored toggles, never the derived flags', async () => {
        await featureFlagsService.updateFeatureFlags({
            ...DEFAULT_FEATURE_FLAGS,
            liveTv: false,
            liveTvEnabled: true,
            internetRadio: false,
            internetRadioEnabled: true,
        });

        expect(savedBody().liveTv).toBe(true);
        expect(savedBody().internetRadio).toBe(true);
    });

    it('turning a feature off still sends off', async () => {
        await featureFlagsService.updateFeatureFlags({
            ...DEFAULT_FEATURE_FLAGS,
            liveTv: true,
            liveTvEnabled: false,
        });

        expect(savedBody().liveTv).toBe(false);
    });

    it('sends the stored discover toggle, not whether a provider has a key', async () => {
        await featureFlagsService.updateFeatureFlags({
            ...DEFAULT_FEATURE_FLAGS,
            discover: false,
            discoverEnabled: true,
        });

        expect(savedBody().discover).toBe(true);
    });

    it('passes the remaining flags through untouched', async () => {
        await featureFlagsService.updateFeatureFlags({
            ...DEFAULT_FEATURE_FLAGS,
            discoverEnabled: false,
            forYou: true,
            releaseCalendar: false,
            dvr: true,
            podcasts: false,
        });

        const body = savedBody();
        expect(body.discover).toBe(false);
        expect(body.forYou).toBe(true);
        expect(body.releaseCalendar).toBe(false);
        expect(body.dvr).toBe(true);
        expect(body.podcasts).toBe(false);
    });

    // subtitleSearch and the two *Enabled fields are read-side only; the update
    // request has no home for them and the server would ignore them anyway.
    it('does not send read-only fields back', async () => {
        await featureFlagsService.updateFeatureFlags(DEFAULT_FEATURE_FLAGS);

        expect(savedBody()).not.toHaveProperty('subtitleSearch');
        expect(savedBody()).not.toHaveProperty('liveTvEnabled');
    });
});

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import FeatureToggle from './FeatureToggle';
import { DEFAULT_FEATURE_FLAGS, type FeatureFlagsVM } from '../../../api/System/featureFlagsService';

const mocks = vi.hoisted(() => ({
    getFeatureFlags: vi.fn(),
    updateFeatureFlags: vi.fn(),
}));

vi.mock('../../../api/System/featureFlagsService', async () => {
    const actual = await vi.importActual<typeof import('../../../api/System/featureFlagsService')>(
        '../../../api/System/featureFlagsService'
    );
    return {
        ...actual,
        featureFlagsService: {
            getFeatureFlags: mocks.getFeatureFlags,
            updateFeatureFlags: mocks.updateFeatureFlags,
        },
    };
});

// Live TV switched on by the admin, but no IPTV source behind it — so the
// server reports the feature itself as not usable.
const onButUnconfigured: FeatureFlagsVM = {
    ...DEFAULT_FEATURE_FLAGS,
    liveTv: false,
    liveTvEnabled: true,
    internetRadio: false,
    internetRadioEnabled: true,
};

const renderToggle = (props: Partial<React.ComponentProps<typeof FeatureToggle>> = {}) => render(
    <FeatureToggle
        featureKey="liveTv"
        label="Enable Live TV"
        description="Live TV description"
        unavailableHint="No Live TV source configured"
        {...props}
    />,
);

describe('FeatureToggle', () => {
    beforeEach(() => {
        mocks.getFeatureFlags.mockReset();
        mocks.updateFeatureFlags.mockReset();
        mocks.updateFeatureFlags.mockResolvedValue(undefined);
    });

    // The switch must show the saved choice. Showing the derived value would
    // read as off on a server with no playlist, and flipping it would appear to
    // do nothing.
    it('shows the stored toggle, not whether the feature is usable', async () => {
        mocks.getFeatureFlags.mockResolvedValue(onButUnconfigured);

        renderToggle();

        await waitFor(() => expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true'));
    });

    it('says why an enabled feature still will not reach clients', async () => {
        mocks.getFeatureFlags.mockResolvedValue(onButUnconfigured);

        renderToggle();

        expect(await screen.findByText('No Live TV source configured')).toBeInTheDocument();
    });

    it('stays quiet once a source exists', async () => {
        mocks.getFeatureFlags.mockResolvedValue({ ...onButUnconfigured, liveTv: true });

        renderToggle();

        await waitFor(() => expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true'));
        expect(screen.queryByText('No Live TV source configured')).toBeNull();
    });

    it('says nothing about sources for a feature that has none', async () => {
        mocks.getFeatureFlags.mockResolvedValue({ ...DEFAULT_FEATURE_FLAGS, podcasts: true });

        renderToggle({ featureKey: 'podcasts', unavailableHint: undefined });

        await waitFor(() => expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true'));
    });

    // The save sends the whole flag set back. If it sent the derived value, an
    // admin turning Live TV off would also silently clear a Radio toggle whose
    // source happened to be unreachable.
    it('saves the stored toggle rather than the derived flag', async () => {
        mocks.getFeatureFlags.mockResolvedValue(onButUnconfigured);

        renderToggle();
        await waitFor(() => expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true'));
        fireEvent.click(screen.getByRole('button'));

        await waitFor(() => expect(mocks.updateFeatureFlags).toHaveBeenCalled());
        const saved = mocks.updateFeatureFlags.mock.calls[0][0] as FeatureFlagsVM;
        expect(saved.liveTvEnabled).toBe(false);
        expect(saved.internetRadioEnabled).toBe(true);
    });

    it('flips the switch it was given, not a neighbouring one', async () => {
        mocks.getFeatureFlags.mockResolvedValue(onButUnconfigured);

        renderToggle({ featureKey: 'internetRadio', unavailableHint: 'No radio source configured' });
        await waitFor(() => expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true'));
        fireEvent.click(screen.getByRole('button'));

        await waitFor(() => expect(mocks.updateFeatureFlags).toHaveBeenCalled());
        const saved = mocks.updateFeatureFlags.mock.calls[0][0] as FeatureFlagsVM;
        expect(saved.internetRadioEnabled).toBe(false);
        expect(saved.liveTvEnabled).toBe(true);
    });
});

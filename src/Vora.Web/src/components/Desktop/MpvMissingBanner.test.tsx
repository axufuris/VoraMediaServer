import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { fakeDesktopBridge } from '../../test/fakeDesktopBridge';
import type { DesktopPlatform } from '../../desktop/desktopBridge';

const MISSING = "The Vora player needs mpv, which isn't installed. Install it with sudo apt install mpv.";

async function renderBanner(platform: DesktopPlatform | null, available = false, reason = MISSING) {
    vi.resetModules();
    if (platform) {
        window.voraDesktop = fakeDesktopBridge({
            platform,
            mpv: { available, version: available ? '0.39.0' : null, reason: available ? null : reason },
        }).bridge;
    }
    const { default: MpvMissingBanner } = await import('./MpvMissingBanner');
    return render(<MpvMissingBanner />);
}

beforeEach(() => {
    localStorage.clear();
});

afterEach(() => {
    delete window.voraDesktop;
});

describe('the mpv missing notice', () => {
    it('tells a Linux user how to install mpv', async () => {
        await renderBanner('linux');

        expect(await screen.findByText('Videos play in the built-in player')).toBeInTheDocument();
        expect(screen.getByText(/sudo apt install mpv/)).toBeInTheDocument();
    });

    it('shows why the mpv on the computer cannot be used, in the words of the shell', async () => {
        await renderBanner('linux', false, 'This computer has mpv 0.35.1, and the Vora player needs 0.37 or newer. Update it with sudo apt install mpv, or install a newer mpv from your distribution.');

        expect(await screen.findByText(/needs 0\.37 or newer/)).toBeInTheDocument();
        expect(screen.queryByText(/isn't installed/)).not.toBeInTheDocument();
    });

    it('stays away once dismissed', async () => {
        const { unmount } = await renderBanner('linux');
        await userEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));
        expect(screen.queryByText('Videos play in the built-in player')).not.toBeInTheDocument();
        unmount();

        await renderBanner('linux');

        expect(screen.queryByText('Videos play in the built-in player')).not.toBeInTheDocument();
    });

    it('is not shown when mpv is there, on Windows, or in a browser', async () => {
        const first = await renderBanner('linux', true);
        expect(screen.queryByText('Videos play in the built-in player')).not.toBeInTheDocument();
        first.unmount();

        const second = await renderBanner('windows');
        expect(screen.queryByText('Videos play in the built-in player')).not.toBeInTheDocument();
        second.unmount();

        await renderBanner(null);
        expect(screen.queryByText('Videos play in the built-in player')).not.toBeInTheDocument();
    });
});

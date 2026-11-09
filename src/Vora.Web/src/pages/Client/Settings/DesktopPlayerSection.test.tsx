import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { desktopSettings, fakeDesktopBridge, type FakeDesktopBridge } from '../../../test/fakeDesktopBridge';
import type { DesktopSettings } from '../../../desktop/desktopBridge';

vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

async function renderSection(fake: FakeDesktopBridge | null) {
    vi.resetModules();
    if (fake) window.voraDesktop = fake.bridge;
    const { default: DesktopPlayerSection } = await import('./DesktopPlayerSection');
    return render(<DesktopPlayerSection />);
}

afterEach(() => {
    delete window.voraDesktop;
});

describe('the desktop player settings', () => {
    it('are not shown in a browser', async () => {
        await renderSection(null);

        expect(screen.queryByText('Desktop player')).not.toBeInTheDocument();
    });

    it('show what the app has saved', async () => {
        await renderSection(fakeDesktopBridge({}, { useMpvForVideo: true, audioPassthrough: 'all', hdrOutput: false, hardwareDecoding: 'off', startFullScreen: false }));

        expect(await screen.findByLabelText('Use the Vora player (mpv) for videos')).toBeChecked();
        expect(screen.getByLabelText('Audio passthrough')).toHaveValue('all');
        expect(screen.getByLabelText('Hardware decoding')).toHaveValue('off');
        expect(screen.getByLabelText('Send HDR to an HDR display')).not.toBeChecked();
        expect(screen.getByLabelText('Start videos in full screen')).not.toBeChecked();
        expect(screen.getByText(/mpv 0\.39\.0/)).toBeInTheDocument();
    });

    it('save a change through the app', async () => {
        const fake = fakeDesktopBridge();
        await renderSection(fake);

        await userEvent.selectOptions(await screen.findByLabelText('Audio passthrough'), 'core');
        await userEvent.click(screen.getByRole('button', { name: 'Save desktop player' }));

        expect(fake.bridge.settings.set).toHaveBeenCalledWith(expect.objectContaining({ audioPassthrough: 'core', useMpvForVideo: true }));
    });

    it('save whether videos start in full screen, so the app remembers it', async () => {
        const fake = fakeDesktopBridge();
        await renderSection(fake);

        const fullScreen = await screen.findByLabelText('Start videos in full screen');
        expect(fullScreen).toBeChecked();
        await userEvent.click(fullScreen);
        await userEvent.click(screen.getByRole('button', { name: 'Save desktop player' }));

        expect(fake.bridge.settings.set).toHaveBeenCalledWith(expect.objectContaining({ startFullScreen: false }));
        expect(await fake.bridge.settings.get()).toMatchObject({ startFullScreen: false });
    });

    it('leave the full screen choice out for an app too old to have it', async () => {
        const { startFullScreen, ...older } = desktopSettings;
        expect(startFullScreen).toBe(true);
        const fake = fakeDesktopBridge({ bridgeVersion: 1, shellVersion: '1.0.0' }, older as DesktopSettings);
        await renderSection(fake);

        await userEvent.selectOptions(await screen.findByLabelText('Audio passthrough'), 'core');
        expect(screen.queryByLabelText('Start videos in full screen')).not.toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Save desktop player' }));

        expect(fake.bridge.settings.set).toHaveBeenCalledWith(expect.not.objectContaining({ startFullScreen: expect.anything() }));
    });

    it('open the folder for advanced mpv settings', async () => {
        const fake = fakeDesktopBridge();
        await renderSection(fake);

        await userEvent.click(await screen.findByRole('button', { name: 'Open mpv settings folder' }));

        expect(fake.bridge.settings.openAdvancedFolder).toHaveBeenCalled();
    });
});

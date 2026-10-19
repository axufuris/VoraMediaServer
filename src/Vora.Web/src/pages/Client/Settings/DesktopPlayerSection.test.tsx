import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { fakeDesktopBridge, type FakeDesktopBridge } from '../../../test/fakeDesktopBridge';

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
        await renderSection(fakeDesktopBridge({}, { useMpvForVideo: true, audioPassthrough: 'all', hdrOutput: false, hardwareDecoding: 'off' }));

        expect(await screen.findByLabelText('Use the Vora player (mpv) for videos')).toBeChecked();
        expect(screen.getByLabelText('Audio passthrough')).toHaveValue('all');
        expect(screen.getByLabelText('Hardware decoding')).toHaveValue('off');
        expect(screen.getByLabelText('Send HDR to an HDR display')).not.toBeChecked();
        expect(screen.getByText(/mpv 0\.39\.0/)).toBeInTheDocument();
    });

    it('save a change through the app', async () => {
        const fake = fakeDesktopBridge();
        await renderSection(fake);

        await userEvent.selectOptions(await screen.findByLabelText('Audio passthrough'), 'core');
        await userEvent.click(screen.getByRole('button', { name: 'Save desktop player' }));

        expect(fake.bridge.settings.set).toHaveBeenCalledWith(expect.objectContaining({ audioPassthrough: 'core', useMpvForVideo: true }));
    });

    it('open the folder for advanced mpv settings', async () => {
        const fake = fakeDesktopBridge();
        await renderSection(fake);

        await userEvent.click(await screen.findByRole('button', { name: 'Open mpv settings folder' }));

        expect(fake.bridge.settings.openAdvancedFolder).toHaveBeenCalled();
    });
});

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';

let pwa: typeof import('../../utils/pwaInstall');
let InstallAppCard: typeof import('./InstallApp')['InstallAppCard'];
let eventListeners: ReturnType<typeof vi.spyOn<typeof window, 'addEventListener'>>;

function offerInstall(outcome: 'accepted' | 'dismissed' = 'dismissed') {
    const prompt = vi.fn().mockResolvedValue(undefined);
    const event = Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
        prompt,
        userChoice: Promise.resolve({ outcome }),
    });
    act(() => { window.dispatchEvent(event); });
    return { event, prompt };
}

describe('Install Vora', () => {
    beforeEach(async () => {
        vi.resetModules();
        vi.stubGlobal('isSecureContext', true);
        vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: false }));
        eventListeners = vi.spyOn(window, 'addEventListener');
        pwa = await import('../../utils/pwaInstall');
        ({ InstallAppCard } = await import('./InstallApp'));
    });

    afterEach(() => {
        for (const [name, listener] of eventListeners.mock.calls) {
            if (name === 'beforeinstallprompt' || name === 'appinstalled') window.removeEventListener(name, listener);
        }
        vi.restoreAllMocks();
        vi.unstubAllGlobals();
    });

    it('keeps a prompt received before settings opens and invokes it only on a click', async () => {
        pwa.initializePwaInstall();
        const { event, prompt } = offerInstall();
        render(<InstallAppCard />);

        expect(event.defaultPrevented).toBe(true);
        expect(prompt).not.toHaveBeenCalled();
        fireEvent.click(screen.getByRole('button', { name: 'Install Vora' }));

        expect(prompt).toHaveBeenCalledTimes(1);
        await waitFor(() => expect(screen.queryByRole('button')).toBeNull());
        await pwa.installPwa();
        expect(prompt).toHaveBeenCalledTimes(1);
    });

    it('updates an already-open settings page when the browser offers installation', () => {
        pwa.initializePwaInstall();
        render(<InstallAppCard />);
        expect(screen.queryByRole('button')).toBeNull();

        offerInstall();

        expect(screen.getByRole('button', { name: 'Install Vora' })).toBeEnabled();
    });

    it('shows browser-menu and iOS instructions when there is no install event', () => {
        pwa.initializePwaInstall();
        render(<InstallAppCard />);

        expect(screen.queryByRole('button')).toBeNull();
        expect(screen.getByRole('status')).toHaveTextContent('Add to Home screen');
        expect(screen.getByRole('status')).toHaveTextContent('Share → Add to Home Screen');
    });

    it('explains the HTTPS requirement on an insecure address', () => {
        vi.stubGlobal('isSecureContext', false);
        pwa.initializePwaInstall();
        render(<InstallAppCard />);

        expect(screen.getByRole('status')).toHaveTextContent('HTTPS address with a trusted certificate');
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('does not offer installation in a standalone app window', () => {
        vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true }));
        pwa.initializePwaInstall();
        render(<InstallAppCard />);

        expect(screen.getByRole('status')).toHaveTextContent('Vora is installed');
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('recognizes an iOS standalone app window', () => {
        Object.defineProperty(navigator, 'standalone', { value: true, configurable: true });
        pwa.initializePwaInstall();
        Reflect.deleteProperty(navigator, 'standalone');
        render(<InstallAppCard />);

        expect(screen.getByRole('status')).toHaveTextContent('Vora is installed');
    });

    it('clears the offer when installed through the browser menu', () => {
        pwa.initializePwaInstall();
        const { prompt } = offerInstall();
        render(<InstallAppCard />);

        act(() => { window.dispatchEvent(new Event('appinstalled')); });

        expect(screen.queryByRole('button')).toBeNull();
        expect(screen.getByRole('status')).toHaveTextContent('Vora is installed');
        expect(prompt).not.toHaveBeenCalled();
    });

    it('does not re-use a consumed prompt and accepts a new browser offer after dismissal', async () => {
        pwa.initializePwaInstall();
        offerInstall();
        render(<InstallAppCard />);
        fireEvent.click(screen.getByRole('button', { name: 'Install Vora' }));
        await waitFor(() => expect(screen.queryByRole('button')).toBeNull());

        const { prompt } = offerInstall();
        fireEvent.click(screen.getByRole('button', { name: 'Install Vora' }));

        expect(prompt).toHaveBeenCalledTimes(1);
        await waitFor(() => expect(screen.queryByRole('button')).toBeNull());
    });

    it('preserves the installed state when appinstalled arrives before userChoice settles', async () => {
        pwa.initializePwaInstall();
        const { event } = offerInstall('accepted');
        let finishChoice: (choice: { outcome: 'accepted' }) => void = () => {};
        event.userChoice = new Promise(resolve => { finishChoice = resolve; });
        render(<InstallAppCard />);
        fireEvent.click(screen.getByRole('button', { name: 'Install Vora' }));
        expect(screen.getByRole('button', { name: 'Installing…' })).toBeDisabled();

        await act(async () => {
            window.dispatchEvent(new Event('appinstalled'));
            finishChoice({ outcome: 'accepted' });
        });

        expect(screen.getByRole('status')).toHaveTextContent('Vora is installed');
    });

    it('shows a useful fallback when the browser rejects the prompt', async () => {
        pwa.initializePwaInstall();
        const { prompt } = offerInstall();
        prompt.mockRejectedValueOnce(new Error('NotAllowedError'));
        vi.spyOn(console, 'warn').mockImplementation(() => {});
        render(<InstallAppCard />);
        fireEvent.click(screen.getByRole('button', { name: 'Install Vora' }));

        await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('could not open its install prompt'));
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('registers its browser event handlers once', () => {
        pwa.initializePwaInstall();
        pwa.initializePwaInstall();

        expect(eventListeners.mock.calls.filter(([name]) => name === 'beforeinstallprompt')).toHaveLength(1);
        expect(eventListeners.mock.calls.filter(([name]) => name === 'appinstalled')).toHaveLength(1);
    });
});

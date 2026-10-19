import { useSyncExternalStore } from 'react';
import { getPwaInstallStatus, installPwa, subscribePwaInstall } from '../../utils/pwaInstall';

export function InstallAppButton() {
    const status = useSyncExternalStore(subscribePwaInstall, getPwaInstallStatus);
    if (status !== 'available' && status !== 'prompting') return null;

    return (
        <button type="button" className="vora-button-secondary text-sm" disabled={status === 'prompting'} onClick={() => { void installPwa(); }}>
            {status === 'prompting' ? 'Installing…' : 'Install Vora'}
        </button>
    );
}

export function InstallAppCard() {
    const status = useSyncExternalStore(subscribePwaInstall, getPwaInstallStatus);
    const message = !window.isSecureContext
        ? 'Open your server’s HTTPS address with a trusted certificate to install Vora.'
        : status === 'installed'
            ? 'Vora is installed. Open it from your app launcher or home screen.'
            : status === 'failed'
                ? 'The browser could not open its install prompt. Try its Install app or Add to Home screen menu option.'
                : status === 'available' || status === 'prompting'
                    ? 'Install Vora to open your library in its own window, directly from your app launcher or home screen.'
                    : 'Use your browser’s Install app or Add to Home screen menu option. On iPhone or iPad, use Share → Add to Home Screen. If Vora is already installed, open it from your app launcher.';

    return (
        <section className="vora-card p-6" aria-labelledby="install-vora-heading">
            <h2 id="install-vora-heading" className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Install Vora</h2>
            <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-muted)' }} role="status">{message}</p>
            <InstallAppButton />
        </section>
    );
}

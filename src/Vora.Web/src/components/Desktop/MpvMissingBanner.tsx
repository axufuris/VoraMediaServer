import { useState } from 'react';
import { useDesktopPlayback } from '../../desktop/useDesktopPlayback';
import { StorageKeys } from '../../utils/storageKeys';

function wasDismissed(): boolean {
    try {
        return localStorage.getItem(StorageKeys.desktopMpvNoticeDismissed) === 'true';
    } catch {
        return false;
    }
}

export default function MpvMissingBanner() {
    const desktop = useDesktopPlayback();
    const [dismissed, setDismissed] = useState(wasDismissed);

    if (dismissed || !desktop.bridge || desktop.bridge.platform !== 'linux' || !desktop.mpv || desktop.mpv.available) return null;

    const dismiss = () => {
        try {
            localStorage.setItem(StorageKeys.desktopMpvNoticeDismissed, 'true');
        } catch (error: unknown) {
            console.warn('Could not remember that the mpv notice was dismissed', error);
        }
        setDismissed(true);
    };

    return (
        <section className="mx-6 mt-4 flex flex-wrap items-center justify-between gap-4 rounded-[var(--vora-radius-lg)] border border-[color-mix(in_srgb,var(--vora-info-500)_45%,transparent)] bg-[var(--vora-info-soft)] px-5 py-4 max-md:mx-3">
            <div className="min-w-0">
                <h2 className="font-semibold text-[var(--vora-text-primary)]">Videos play in the built-in player</h2>
                <p className="text-sm text-[var(--vora-text-secondary)]">
                    The Vora player needs mpv, which isn't installed. {desktop.mpv.reason ?? 'Install mpv from your distribution.'} Once it is, videos open in the Vora player with no restart.
                </p>
            </div>
            <button type="button" onClick={dismiss} className="vora-button-secondary cursor-pointer">Dismiss</button>
        </section>
    );
}

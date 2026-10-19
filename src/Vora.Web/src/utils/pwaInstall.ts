interface InstallPromptEvent extends Event {
    prompt(): Promise<void>;
    userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

type InstallStatus = 'unavailable' | 'available' | 'prompting' | 'installed' | 'failed';

let status: InstallStatus = 'unavailable';
let deferredPrompt: InstallPromptEvent | null = null;
let initialized = false;
const listeners = new Set<() => void>();

function setStatus(next: InstallStatus): void {
    status = next;
    listeners.forEach(listener => listener());
}

export function initializePwaInstall(): void {
    if (initialized) return;
    initialized = true;

    if (window.matchMedia?.('(display-mode: standalone)').matches
        || (navigator as Navigator & { standalone?: boolean }).standalone) {
        setStatus('installed');
        return;
    }

    window.addEventListener('beforeinstallprompt', event => {
        event.preventDefault();
        deferredPrompt = event as InstallPromptEvent;
        setStatus('available');
    });

    window.addEventListener('appinstalled', () => {
        deferredPrompt = null;
        setStatus('installed');
    });
}

export function subscribePwaInstall(listener: () => void): () => void {
    listeners.add(listener);
    return () => { listeners.delete(listener); };
}

export function getPwaInstallStatus(): InstallStatus {
    return status;
}

export async function installPwa(): Promise<void> {
    if (!deferredPrompt || status !== 'available') return;
    const event = deferredPrompt;
    deferredPrompt = null;
    setStatus('prompting');

    try {
        await event.prompt();
        await event.userChoice;
        if (getPwaInstallStatus() !== 'installed' && !deferredPrompt) setStatus('unavailable');
    } catch (error) {
        if (getPwaInstallStatus() !== 'installed' && !deferredPrompt) setStatus('failed');
        console.warn('Vora could not open the browser install prompt.', error);
    }
}

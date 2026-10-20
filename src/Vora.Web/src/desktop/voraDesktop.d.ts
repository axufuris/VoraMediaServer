import type { VoraDesktopBridge } from './desktopBridge';

declare global {
    interface Window {
        voraDesktop?: VoraDesktopBridge;
    }
}

export {};

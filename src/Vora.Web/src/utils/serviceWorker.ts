export function registerServiceWorker(): void {
    if (!import.meta.env.PROD || !window.isSecureContext || !('serviceWorker' in navigator)) return;

    navigator.serviceWorker.register('/sw.js').catch(error => {
        console.warn('Vora could not register its service worker. Check that /sw.js is reachable through your reverse proxy.', error);
    });
}

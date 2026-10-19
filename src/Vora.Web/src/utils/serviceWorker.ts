// Registers /sw.js so a browser can offer to install Vora as an app. Browsers
// allow a service worker only on a secure origin (HTTPS, or localhost), so on a
// plain-HTTP server address this does nothing. The development server has none,
// because Vite serves the app differently from the built one.
export function registerServiceWorker(): void {
    if (!import.meta.env.PROD || !window.isSecureContext || !('serviceWorker' in navigator)) return;

    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/sw.js').catch(() => {
            console.warn('Vora could not register its service worker, so the browser will not offer to install it.');
        });
    }, { once: true });
}

// The default API base used when no server is resolved from the vault or the
// pending-login URL. In production the SPA is served by the Vora API on the same
// origin, so default to that origin — baking `localhost:5000` into a vault entry
// (as the legacy-migration and auto-login-bypass paths used to) breaks the client
// for anyone not browsing from localhost. `localhost:5000` stays the default only
// for local dev, where Vite serves the SPA separately from the API.
export function defaultApiBaseUrl(): string {
    const configured = import.meta.env.VITE_API_BASE_URL;
    if (configured) return configured;

    const host = typeof window !== 'undefined' ? window.location.hostname : '';
    if (host && host !== 'localhost' && host !== '127.0.0.1') {
        return `${window.location.origin}/api`;
    }

    return 'http://localhost:5000/api';
}

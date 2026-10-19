// Vora's service worker. It caches nothing: every page load goes to the server,
// so an updated server is never answered with an old copy of the app. It only
// answers a page load that cannot reach the server, with a page that says so.
// API calls, artwork and video never pass through it.

const offlinePage = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="theme-color" content="#08080b">
<title>Vora</title>
<style>
  html, body { margin: 0; height: 100%; background: #08080b; color: #f4f4f5; font-family: system-ui, -apple-system, "Segoe UI", sans-serif; }
  main { min-height: 100%; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 16px; padding: 24px; box-sizing: border-box; text-align: center; }
  h1 { margin: 0; font-size: 1.4rem; font-weight: 600; }
  p { margin: 0; max-width: 28rem; color: #a1a1aa; line-height: 1.5; }
  button { margin-top: 8px; padding: 10px 22px; border: 0; border-radius: 999px; background: #f59e0b; color: #08080b; font: inherit; font-weight: 600; cursor: pointer; }
</style>
</head>
<body>
<main>
  <svg width="72" height="72" viewBox="0 0 64 64" aria-hidden="true"><defs><linearGradient id="v" x1="0.1" y1="0" x2="0.55" y2="1"><stop offset="0" stop-color="#fdba74"/><stop offset="0.55" stop-color="#fb923c"/><stop offset="1" stop-color="#ea580c"/></linearGradient></defs><path d="M6 8 L18 8 L32 40 L46 8 L58 8 L32 60 Z" fill="url(#v)"/></svg>
  <h1>Vora can't reach your server</h1>
  <p>Check that you're connected to the internet, or to the network your Vora server is on, then try again.</p>
  <button type="button" onclick="location.reload()">Try again</button>
</main>
</body>
</html>`;

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('fetch', event => {
    if (event.request.mode !== 'navigate') return;
    event.respondWith(
        fetch(event.request).catch(() => new Response(offlinePage, { headers: { 'Content-Type': 'text/html; charset=utf-8' } })),
    );
});

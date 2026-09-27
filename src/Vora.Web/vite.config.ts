import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [
        react(),
        tailwindcss(),
    ],
    server: {
        port: 5173, // Locks the UI to port 5173 so your .NET CORS policy doesn't break
        strictPort: true,
        proxy: {
            // Intercepts any API calls and forwards them to the Docker container
            '/api': {
                target: 'http://localhost:8080',
                changeOrigin: true,
                secure: false // Ignores SSL certificate errors during local development
            },
            // NEW: Intercepts SignalR traffic and forwards the WebSockets to Docker!
            '/hubs': {
                target: 'http://localhost:8080',
                changeOrigin: true,
                secure: false,
                ws: true // CRITICAL: Tells Vite to keep the WebSocket connection alive!
            }
        }
    },
    build: {
        // vendor-hls is intentionally ~500 KB but is loaded lazily (only when
        // playback starts), so a slight bump keeps the warning meaningful for
        // anything *unexpectedly* large in the future.
        chunkSizeWarningLimit: 600,
        // Vendor splits: stable third-party libraries get their own chunks so they
        // cache across releases. Any node_modules import not matched explicitly
        // falls through to the catch-all 'vendor' chunk, so new deps don't need
        // a code change to be cached cleanly.
        rollupOptions: {
            output: {
                manualChunks(id) {
                    if (!id.includes('node_modules')) return undefined;
                    if (id.includes('react-rnd')) return 'vendor-react-rnd';
                    if (id.includes('react-router')) return 'vendor-react';
                    if (id.match(/[\\/]node_modules[\\/](react|react-dom|scheduler)[\\/]/)) return 'vendor-react';
                    if (id.includes('@microsoft/signalr')) return 'vendor-signalr';
                    if (id.includes('hls.js')) return 'vendor-hls';
                    if (id.includes('axios')) return 'vendor-axios';
                    return 'vendor';
                }
            }
        }
    }
})
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
    }
})
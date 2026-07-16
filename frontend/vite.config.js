import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';

// In production Caddy serves this app on :80 and reverse-proxies /api to
// Kestrel on :5000. The dev server mirrors that shape, so the same relative
// /api URLs work both here and behind Caddy.
export default defineConfig({
    plugins: [plugin()],
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./src', import.meta.url))
        }
    },
    build: {
        // Caddy serves this folder directly (see the Caddyfile at the repo root).
        outDir: '../deploy/web',
        emptyOutDir: true
    },
    server: {
        port: 5173,
        proxy: {
            '/api': {
                target: 'http://localhost:5000',
                changeOrigin: true
            }
        }
    }
});

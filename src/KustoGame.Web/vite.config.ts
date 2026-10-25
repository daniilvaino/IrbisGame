import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The Kusto language service is Bridge.NET output: it registers itself on `global`.
const globalShim = { global: 'globalThis' };

export default defineConfig({
    plugins: [react()],
    define: globalShim,
    optimizeDeps: { esbuildOptions: { define: globalShim } },
    worker: { format: 'iife' },
    build: {
        // `dotnet run` serves the built app from the server's wwwroot.
        outDir: '../KustoGame.Server/wwwroot',
        emptyOutDir: true,
        chunkSizeWarningLimit: 12000,
    },
    server: {
        port: 5173,
        proxy: { '/api': 'http://localhost:5080' },
    },
});

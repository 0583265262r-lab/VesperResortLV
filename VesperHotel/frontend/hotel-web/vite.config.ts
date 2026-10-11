import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  // The project works without dependency pre-bundling. This also keeps the
  // development server compatible with restricted Windows workspaces.
  optimizeDeps: { noDiscovery: true, include: [] },
  server: { proxy: { '/api': 'http://127.0.0.1:5080' } },
});

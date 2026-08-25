import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// M1 skeleton: dev proxy to the local API (no business logic yet).
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/health': 'http://localhost:5080',
    },
  },
});

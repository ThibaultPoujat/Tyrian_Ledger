import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// M1 skeleton: dev proxy to the local API (no business logic yet).
// The API must be started with `dotnet run --project src/Gw2Tp.Web`;
// by default it serves http://localhost:5000 (override with API_URL if changed).
const apiTarget = process.env.API_URL ?? 'http://localhost:5000';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/healthz': apiTarget,
    },
  },
});

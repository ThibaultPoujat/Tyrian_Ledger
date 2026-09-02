import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  base: process.env.VITE_SITE_BASE_PATH?.trim() || '/',
  plugins: [react()],
});

import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), 'VITE_');

  return {
    plugins: [react()],
    server: {
      proxy: {
        '/api': {
          target: environment.VITE_LOCAL_API_ORIGIN ?? 'http://127.0.0.1:5080',
          changeOrigin: false,
        },
      },
    },
  };
});

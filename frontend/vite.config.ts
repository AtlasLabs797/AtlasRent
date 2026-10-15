import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// DOC_TECNICA §1: la API sirve el build como estático desde wwwroot.
export default defineConfig({
  plugins: [react()],
  // Solo en desarrollo: el cliente HTTP llama a '/api/v1' en el mismo origen, así que el servidor de
  // desarrollo tiene que reenviarlo a la API. En producción no hace falta: la API sirve el build.
  server: {
    proxy: {
      '/api': { target: 'http://127.0.0.1:5100', changeOrigin: false },
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
  },
});
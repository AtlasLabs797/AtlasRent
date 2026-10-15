import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// DOC_TECNICA §1: la API sirve el build como estático desde wwwroot.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
  },
});
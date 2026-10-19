/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Il display gira nella WebView2 del Player (Chromium aggiornato), ma alcune casse hanno ancora
// Windows 10 con runtime più vecchi: chrome109 è l'ultimo Chromium per Windows 7/8.1.
export default defineConfig({
  plugins: [react()],
  build: {
    target: ['chrome109', 'edge109'],
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.{ts,tsx}'],
    // Senza, Vitest sostituisce i CSS con stringhe vuote anche con ?raw (serve a tokens.test.ts).
    css: true,
  },
});

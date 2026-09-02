/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // A capability-gated route re-authorizes on entry before rendering anything
    // (#146/#148), so mounting one is asynchronous by design. Both bounds moved
    // together: a single wait now costs more, and a test doing several of them
    // needs headroom. Neither bound is removed — a gate that never opens still
    // fails, just later.
    testTimeout: 15_000,
  },
})

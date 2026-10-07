import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:7211',
        secure: false,
      },
      '/health': {
        target: 'https://localhost:7211',
        secure: false,
      },
    },
  },
  test: {
    environment: 'jsdom',
  },
})

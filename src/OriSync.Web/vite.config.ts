import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5211',
      '/health': 'http://localhost:5211',
    },
  },
  test: {
    environment: 'jsdom',
  },
})

import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: { host: '127.0.0.1', port: 5173, strictPort: true, proxy: { '/api': process.env.VITE_API_TARGET ?? 'http://127.0.0.1:5080' } },
  preview: { host: '127.0.0.1', port: 4173, strictPort: true },
  test: { environment: 'jsdom', include: ['src/**/*.test.ts'], restoreMocks: true },
})

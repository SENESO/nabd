import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: { outDir: '../src/Nabd.Server/wwwroot', emptyOutDir: true },
  server: { proxy: { '/api': 'http://localhost:5199' } },
})

import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { readFileSync } from 'fs'
import { resolve } from 'path'

const { version } = JSON.parse(readFileSync(resolve(__dirname, 'package.json'), 'utf-8'))

// SPA собирается прямо в wwwroot бэкенда — NestJS-аналог: ASP.NET Core отдаёт статику.
// В dev — проксируем API/Swagger на Kestrel (порт 5099).
export default defineConfig({
  plugins: [
    react(),
    {
      name: 'html-title-version',
      transformIndexHtml(html) {
        return html.replace(
          /<title>.*?<\/title>/,
          `<title>WinAdmin v${version} — управление машиной</title>`,
        )
      },
    },
  ],
  define: {
    __APP_VERSION__: JSON.stringify(version),
  },
  build: {
    outDir: '../backend/WinAdmin.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5188,
    proxy: {
      '/api': 'http://localhost:5099',
      '/health': 'http://localhost:5099',
      '/swagger': 'http://localhost:5099',
    },
  },
})

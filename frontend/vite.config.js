import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import seoPrerender from './seo-plugin.js'
import { versionDefine } from './build-version.js'

// https://vite.dev/config/
export default defineConfig(async () => ({
  plugins: [react(), seoPrerender()],
  // The current app version, looked up at build time (see build-version.js).
  define: await versionDefine(),
}))

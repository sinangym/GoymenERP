/**
 * F-DESKTOP-009 — test-local Vite start-up profile for the REAL F008 React app.
 *
 * The application's own `vite.config.ts` is imported unchanged and only the dev
 * server binding is overridden: loopback host, a fixed port, and the `/api` proxy
 * target taken from the environment. The application sources are not touched.
 *
 *  - T lane (controlled responses): every `/api/v1/**` request is intercepted in
 *    the browser by the Playwright harness, so the proxy target is a closed port
 *    on loopback — a request that escaped interception fails visibly instead of
 *    reaching anything.
 *  - L lane (live API): `CARI009_API_TARGET` is the loopback API base recorded in
 *    the C-DESKTOP-006 environment manifest.
 *
 * Nothing here is imported from `vite`: this file lives outside the frontend
 * package and must not depend on a second copy of Vite.
 */
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import base from '../../../src/frontend/goymen-erp-web/vite.config.ts'

const here = path.dirname(fileURLToPath(import.meta.url))
const frontendRoot = path.resolve(here, '../../../src/frontend/goymen-erp-web')
const port = Number(process.env.CARI009_UI_PORT ?? '15173')
const apiTarget = process.env.CARI009_API_TARGET ?? 'http://127.0.0.1:9'

const baseConfig = base as Record<string, unknown> & { server?: Record<string, unknown> }

export default {
  ...baseConfig,
  root: frontendRoot,
  server: {
    ...baseConfig.server,
    host: '127.0.0.1',
    port,
    strictPort: true,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
      },
    },
  },
}

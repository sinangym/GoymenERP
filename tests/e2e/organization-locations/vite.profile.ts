/**
 * Test-local Vite start-up profile for the real Organization screens.
 *
 * The application's own `vite.config.ts` is imported unchanged and only the dev-server
 * binding is overridden: loopback host, the pinned Organization UI port and the `/api`
 * proxy target from the environment manifest. No application source is touched.
 *
 * NOTHING IS IMPORTED FROM `vite`. This file lives outside the frontend package and must
 * not depend on a second copy of Vite — the same rule the C-DESKTOP-009 profile follows.
 *
 * Keeping the browser same-origin through the proxy is not cosmetic here: the replay
 * headers these journeys read (`Idempotency-Replayed`,
 * `Idempotency-Original-Correlation-Id`) reach the page without any shared CORS change.
 *
 * `strictPort` is a safety property, not a preference. A UI that silently moved to
 * another port could land on the Cari (25xxx) or Ürün (26xxx) reservation, and the run
 * would then be disturbing an environment it does not own.
 */
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import base from '../../../src/frontend/goymen-erp-web/vite.config.ts'

const here = path.dirname(fileURLToPath(import.meta.url))
const frontendRoot = path.resolve(here, '../../../src/frontend/goymen-erp-web')
const port = Number(process.env.ORG_UI_PORT ?? '27173')
/**
 * A closed loopback port by default, so a run that was not given the manifest's API base
 * fails visibly instead of reaching some other lane's API.
 */
const apiTarget = process.env.ORG_API_TARGET ?? 'http://127.0.0.1:9'

/**
 * `defineConfig` also accepts a function or a promise. Today the application config is a
 * plain object, so spreading it preserves `plugins: [react()]` — but if it ever becomes
 * the function form, a spread would silently yield `{}`, the React plugin would vanish
 * and the app would fail in the BROWSER rather than here. Checking costs one line and
 * moves that failure to load time with a message that says what to do.
 */
if (typeof base !== 'object' || base === null) {
  throw new Error(
    'the application vite config is no longer a plain object (defineConfig now returns a ' +
      'function or promise); this profile must resolve it before overriding the server block',
  )
}

const baseConfig = base as Record<string, unknown> & {
  server?: Record<string, unknown> & { proxy?: Record<string, unknown> }
}

export default {
  ...baseConfig,
  root: frontendRoot,
  server: {
    ...baseConfig.server,
    host: '127.0.0.1',
    port,
    strictPort: true,
    proxy: {
      ...baseConfig.server?.proxy,
      '/api': { target: apiTarget, changeOrigin: true },
    },
  },
}

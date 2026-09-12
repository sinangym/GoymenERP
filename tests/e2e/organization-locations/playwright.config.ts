/**
 * Organization real-browser acceptance runner — ORG-01 / ORG-02 against a LIVE API.
 *
 * There is one lane and it is live. This suite has no controlled-response mode on
 * purpose: the component/DOM behaviour is already covered by
 * `src/organization/**.test.tsx` (177 cases), and a second fixture lane here would add
 * confidence about nothing while looking like browser evidence. What only a real
 * browser against a real API can show is the part those tests cannot reach — a 204 that
 * carries no state, a server-side replay of a real key, a real version conflict, and
 * the shell's own capability/auth boundaries.
 *
 * IT REFUSES TO RUN WITHOUT THE TUPLE. No ambient defaults, no fallback port, no
 * "assume ready". `readOrganizationManifest()` throws unless the Organization backend
 * owner has declared ENV_READY and named every bound commit, worktree and owned
 * resource. A run that cannot say which code it served is not evidence about that code.
 *
 * PORTS ARE PINNED, NOT PREFERRED. 27173 UI / 27080 API are this lane's reservation;
 * Cari (25xxx) and Ürün (26xxx) belong to other owners and are never touched. An
 * override that disagrees is refused here rather than becoming a confusing failure — or
 * worse, a quiet success against someone else's process.
 *
 * One worker, no retries: an acceptance case that passes on the second attempt has not
 * shown the behaviour it claims.
 */
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'
import { ORG_API_PORT, ORG_UI_PORT, readOrganizationManifest } from './src/manifest.js'

const here = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(here, '../../..')
const frontendDir = path.join(repoRoot, 'src', 'frontend', 'goymen-erp-web')
const viteBin = path.join(frontendDir, 'node_modules', 'vite', 'bin', 'vite.js')
const profile = path.join(here, 'vite.profile.ts')
const evidenceDir = process.env.ORG_EVIDENCE_DIR ?? path.join(here, 'artifacts')

/** Reading it here means a missing tuple stops the run before Vite or a browser starts. */
const manifest = readOrganizationManifest()

const requestedUiPort = process.env.ORG_UI_PORT
if (requestedUiPort !== undefined && Number(requestedUiPort) !== ORG_UI_PORT) {
  throw new Error(
    `this lane is pinned to UI port ${ORG_UI_PORT}; ORG_UI_PORT=${requestedUiPort} would ` +
      'either collide with another lane or serve a UI the owned API does not expect',
  )
}

export default defineConfig({
  globalSetup: path.join(here, 'global-setup.ts'),
  testDir: path.join(here, 'specs'),
  testMatch: /(^|[\\/])o-[^\\/]*\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(evidenceDir, 'playwright-output'),
  reporter: [
    ['list'],
    ['junit', { outputFile: path.join(evidenceDir, 'junit.xml') }],
    ['json', { outputFile: path.join(evidenceDir, 'results.json') }],
  ],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: `http://127.0.0.1:${ORG_UI_PORT}`,
    viewport: { width: 1280, height: 800 },
    locale: 'tr-TR',
    trace: 'on',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  webServer: {
    command: `node "${viteBin}" --config "${profile}" --host 127.0.0.1 --port ${ORG_UI_PORT} --strictPort`,
    cwd: frontendDir,
    url: `http://127.0.0.1:${ORG_UI_PORT}/`,
    reuseExistingServer: false,
    timeout: 120_000,
    stdout: 'pipe',
    stderr: 'pipe',
    /**
     * ONLY what the Vite profile needs. Playwright merges this over the process
     * environment and the JSON reporter serializes it into `results.json`, so spreading
     * `process.env` here would publish the whole environment into an artifact. Nothing
     * below is a secret: a loopback port and the default development actor.
     */
    env: {
      ORG_UI_PORT: String(ORG_UI_PORT),
      ORG_API_TARGET: manifest.apiBaseUrl,
      VITE_DEV_USER: manifest.operatorActor,
      VITE_API_BASE_URL: '/api/v1',
    },
  },
})

export { ORG_API_PORT }

/**
 * F-DESKTOP-009 — real-browser acceptance runner for C-02 create + CARI-LINK.
 *
 * Two lanes, selected by `CARI009_LANE`:
 *  - `T` (default): controlled responses. The REAL F008 React application runs in
 *    a real Chromium under Vite; every `/api/v1/**` request is answered in the
 *    browser by the Playwright harness. Nothing here is jsdom.
 *  - `L`: live API. No route.fulfill — the Vite proxy forwards `/api` to the
 *    loopback API of the C-DESKTOP-006 environment manifest.
 *
 * One worker, no retries: an acceptance case that passes on the second attempt
 * has not shown the behaviour it claims. Traces and screenshots are kept as
 * evidence; the data is synthetic and no secret is involved.
 */
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

const here = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(here, '../../..')
const frontendDir = path.join(repoRoot, 'src', 'frontend', 'goymen-erp-web')
const lane = (process.env.CARI009_LANE ?? 'T').toUpperCase()
const uiPort = Number(process.env.CARI009_UI_PORT ?? '15173')
const evidenceDir = process.env.CARI009_EVIDENCE_DIR ?? path.join(here, 'artifacts')
const laneDir = path.join(evidenceDir, lane)
const viteBin = path.join(frontendDir, 'node_modules', 'vite', 'bin', 'vite.js')
const profile = path.join(here, 'vite.profile.ts')

export default defineConfig({
  globalSetup: path.join(here, 'global-setup.ts'),
  testDir: path.join(here, 'specs'),
  // Anchored to the file name: an unanchored `l-` also matched "termina|l-|failure".
  testMatch: lane === 'L' ? /(^|[\\/])l-[^\\/]*\.spec\.ts$/ : /(^|[\\/])t-[^\\/]*\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(laneDir, 'playwright-output'),
  reporter: [
    ['list'],
    ['junit', { outputFile: path.join(laneDir, 'junit.xml') }],
    ['json', { outputFile: path.join(laneDir, 'results.json') }],
  ],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: `http://127.0.0.1:${uiPort}`,
    viewport: { width: 1280, height: 800 },
    locale: 'tr-TR',
    trace: 'on',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  webServer: {
    command: `node "${viteBin}" --config "${profile}" --host 127.0.0.1 --port ${uiPort} --strictPort`,
    cwd: frontendDir,
    url: `http://127.0.0.1:${uiPort}/`,
    reuseExistingServer: false,
    timeout: 120_000,
    stdout: 'pipe',
    stderr: 'pipe',
    // ONLY the variables the Vite profile needs. Playwright merges this object over the
    // process environment when it spawns the server, so spreading `process.env` here is
    // never necessary — and the JSON reporter serializes `webServer.env` into
    // results.json, which is how F-DESKTOP-009's reports came to contain the whole
    // process environment (F-DESKTOP-010 remediation). Nothing here is a secret.
    env: webServerEnv(),
  },
})

function webServerEnv(): Record<string, string> {
  const env: Record<string, string> = {
    CARI009_UI_PORT: String(uiPort),
    CARI009_API_TARGET: process.env.CARI009_API_TARGET ?? 'http://127.0.0.1:9',
  }
  // The synthetic bootstrap actor name (published in the environment manifest), L lane only.
  if (process.env.VITE_DEV_USER) env.VITE_DEV_USER = process.env.VITE_DEV_USER
  return env
}

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
import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

const here = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(here, '../../..')
const frontendDir = path.join(repoRoot, 'src', 'frontend', 'goymen-erp-web')
const lane = (process.env.CARI009_LANE ?? 'T').toUpperCase()
/**
 * The UI port is a SOURCE-profile condition, not a preference.
 *
 * C006's isolated Testing host pins its CORS policy to exactly
 * `http://127.0.0.1:25173` and `http://localhost:25173` (AcceptanceFactory.cs), and the
 * environment envelope carries no UI field at all — there is nothing to read it from.
 * So the L lane binds 25173 and refuses an ambient override that disagrees: a UI served
 * anywhere else is refused by the API, and quietly moving to another port would turn
 * that into a confusing CORS failure instead of a clear one here. `--strictPort` below
 * covers the other half, occupation.
 */
const PINNED_UI_PORT = 25173
const uiPort = resolveUiPort()

function resolveUiPort(): number {
  const requested = process.env.CARI009_UI_PORT
  // The T lane answers every request in the browser and never reaches an API, so it keeps
  // its own port: serving controlled responses on the one origin the live API trusts is
  // an avoidable way to confuse two runs.
  if (lane !== 'L') return Number(requested ?? 15173)
  if (requested !== undefined && Number(requested) !== PINNED_UI_PORT) {
    throw new Error(`the L lane is pinned to UI port ${PINNED_UI_PORT} by the C006 source profile; CARI009_UI_PORT=${requested} would be refused by the API`)
  }
  return PINNED_UI_PORT
}
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
  /**
   * In the L lane the API target and the bootstrap actor come from the C-DESKTOP-006
   * environment manifest, not from ambient variables.
   *
   * The values used to be whatever the caller happened to export, with
   * `http://127.0.0.1:9` as the fallback — a port nothing listens on, chosen so a
   * misconfigured run fails instead of silently reaching some other API. That
   * fallback stays for the T lane, which has no live API at all. What changes is
   * that the L lane no longer ACCEPTS an ambient value: a run that proxies somewhere
   * other than the owned environment is not evidence about it, and reading the
   * manifest is the only way to know which one this is.
   */
  const env: Record<string, string> = {
    CARI009_UI_PORT: String(uiPort),
    CARI009_API_TARGET: process.env.CARI009_API_TARGET ?? 'http://127.0.0.1:9',
  }
  if (lane === 'L') {
    const manifestFile = process.env.CARI009_ENVIRONMENT_MANIFEST
    if (!manifestFile) {
      throw new Error('CARI009_ENVIRONMENT_MANIFEST is not set; the L lane needs the C-DESKTOP-006 environment.json')
    }
    const manifest = JSON.parse(readFileSync(manifestFile, 'utf8')) as {
      readyForBrowser?: boolean
      apiBaseUrl?: string
      actor?: string
    }
    if (manifest.readyForBrowser !== true) throw new Error('environment manifest is not readyForBrowser')
    if (!manifest.apiBaseUrl) throw new Error('environment manifest is missing apiBaseUrl')
    if (!manifest.actor) throw new Error('environment manifest is missing actor')
    env.CARI009_API_TARGET = manifest.apiBaseUrl
    env.VITE_DEV_USER = manifest.actor
    return env
  }
  // T lane: no live API, and no manifest to read one from.
  if (process.env.VITE_DEV_USER) env.VITE_DEV_USER = process.env.VITE_DEV_USER
  return env
}

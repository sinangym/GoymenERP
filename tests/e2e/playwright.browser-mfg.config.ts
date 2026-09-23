import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

/**
 * MFG real-browser acceptance in the selectable BROWSER-CI workflow (`browser:mfg:ci`).
 *
 * WHAT IT RUNS: `manufacturing-mfg/specs/mfg-*-journey.spec.ts` — the production order and plan screens
 * against the disposable stack the workflow built from the exact head: real API,
 * real SQL, the real Settings kilogram profile, the authorized web profile. It WRITES (facts on
 * orders it arranges itself through the real API), so it is a writing suite and checks its own
 * post-conditions through the API.
 *
 * THE CONTRACT (tests/e2e/browser-ci/README.md), no defaults. The suite's own binding is
 * `BROWSER_E2E_MFG_SEED` (manufacturing-mfg/src/env.ts): actors and ids written by the stage-2
 * seed step with their real Inventory parent roll and synthetic grants, on the `normal` capability profile (MFG-UI-F: the screen is
 * published; first accepted on `mfg-staged`, card D4). Without them the run refuses.
 *
 * Retries 0 and forbidOnly; traces, screenshots and video off (the C-01 artifact-privacy decision).
 */
function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the mfg suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

const here = path.dirname(fileURLToPath(import.meta.url))
const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: path.join(here, 'manufacturing-mfg', 'specs'),
  testMatch: /(^|[\\/])mfg-(order|plan)-journey\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(artifactsDir, 'browser-mfg-playwright-output'),
  reporter: [['list'], ['junit', { outputFile: required('BROWSER_E2E_JUNIT_FILE') }]],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: required('BROWSER_E2E_AUTHORIZED_BASE_URL'),
    viewport: { width: 1400, height: 900 },
    locale: 'tr-TR',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})

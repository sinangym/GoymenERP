import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

/**
 * Logistics suite `l04` (`browser:l04:ci`) — LOG-4b user-flow acceptance of the loading-instruction
 * screens (L-02 list, L-03 new, L-02-DETAIL detail with the record-load form) in the selectable BROWSER-CI
 * workflow: a real browser against the disposable stack built from the exact head — real API, real SQL, the
 * suite's capability profile (`log-staged` first, the FIN-UI-1c pattern; `normal` after the flip), the
 * authorized web.
 *
 * It WRITES: the positive path creates and issues an instruction from a real selection of the seeded
 * approved Sales line and records loads on it; the negatives use the seeded Issued instruction
 * (`BROWSER_E2E_L04_SEED`, logistics-l04/src/env.ts). Every step is re-read through the real API. The
 * cases are one serial journey; `fullyParallel: false`, `workers: 1`, file order.
 *
 * The contract (tests/e2e/browser-ci/README.md), no defaults. Retries 0 and forbidOnly; traces,
 * screenshots and video off (the C-01 artifact-privacy decision).
 */
function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the l04 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

const here = path.dirname(fileURLToPath(import.meta.url))
const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: path.join(here, 'logistics-l04', 'specs'),
  testMatch: /(^|[\\/])l04-loading-journey\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(artifactsDir, 'logistics-l04-playwright-output'),
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

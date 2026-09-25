import { defineConfig, devices } from '@playwright/test'

/**
 * Sales suite `s03` (`browser:s03:ci`) — BROWSER-CI stage 1 plumbing of the Sales order-draft
 * screens, honouring the workflow's suite contract exactly as `playwright.browser-smoke.config.ts`:
 * BROWSER_E2E_AUTHORIZED_BASE_URL / BROWSER_E2E_UNAUTHORIZED_BASE_URL, BROWSER_E2E_API_BASE_URL,
 * BROWSER_E2E_ARTIFACTS_DIR, BROWSER_E2E_JUNIT_FILE. No defaults: without the disposable stack the
 * suite fails, it never tests something else.
 *
 * v2a (S03 slice) runs on the `normal` profile against the REAL API and is explicitly negative
 * evidence: the granted actor reads the real empty list, a create attempt is refused honestly and
 * writes nothing, the ungranted profile is closed out. It needs the shell mount, the published
 * Sales capabilities and a disposable grant (BROWSER-CI stage 2, real services only). v2b (the
 * positive create → detail → list → replay → reopen path) waits for the Product activation path
 * and the GDC data. `retries: 0`, no trace/screenshot/video: artifacts are privacy-checked.
 */

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the Sales s03 suite runs only inside the disposable stack.`)
  }
  return value
}

const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: './sales-s03',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  outputDir: `${artifactsDir}/sales-s03-playwright-output`,
  reporter: [['list'], ['junit', { outputFile: required('BROWSER_E2E_JUNIT_FILE') }]],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: required('BROWSER_E2E_AUTHORIZED_BASE_URL'),
    viewport: { width: 1400, height: 900 },
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})

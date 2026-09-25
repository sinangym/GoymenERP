import { defineConfig, devices } from '@playwright/test'

/**
 * Accounting suite `acc01` (`browser:acc01:ci`) — the ACC-01 user path (ACC-V0b, M-07/M-04/M-01) on the
 * disposable stack, honouring the workflow's suite contract: BROWSER_E2E_AUTHORIZED_BASE_URL /
 * BROWSER_E2E_UNAUTHORIZED_BASE_URL, BROWSER_E2E_ARTIFACTS_DIR, BROWSER_E2E_JUNIT_FILE and the
 * suite's BROWSER_E2E_ACC01_SEED. No defaults: without the disposable stack the suite fails, it
 * never tests something else. Serial, one worker: ACC01-02 approves what ACC01-01 proposed.
 * `retries: 0`, no trace/screenshot/video: artifacts are privacy-checked.
 */

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the Accounting acc01 suite runs only inside the disposable stack.`)
  }
  return value
}

const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: './accounting-acc01/specs',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: `${artifactsDir}/accounting-acc01-playwright-output`,
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

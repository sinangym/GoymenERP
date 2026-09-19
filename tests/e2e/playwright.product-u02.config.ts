import { defineConfig, devices } from '@playwright/test'

/**
 * Product suite `u02` (`browser:u02:ci`) — the U-02 new-product screen on the disposable stack, `normal`
 * profile plus infra/browser-suites/u02.override.yml, after the u02 fixture (Suites/U02) seeded Product
 * master authority for the authorized actor and one synthetic code group through the gated writers.
 * It honours the workflow's suite contract exactly as the smoke and s03 configs: BROWSER_E2E_AUTHORIZED_BASE_URL
 * / BROWSER_E2E_UNAUTHORIZED_BASE_URL, BROWSER_E2E_API_BASE_URL, BROWSER_E2E_ARTIFACTS_DIR,
 * BROWSER_E2E_JUNIT_FILE and the fixture's BROWSER_E2E_U02_SEED. No defaults: without the disposable stack
 * and its seed the suite fails, it never tests something else. `retries: 0`, no trace/screenshot/video:
 * artifacts are privacy-checked.
 */

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the Product u02 suite runs only inside the disposable stack.`)
  }
  return value
}

const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: './product-u02-suite',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  outputDir: `${artifactsDir}/product-u02-playwright-output`,
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

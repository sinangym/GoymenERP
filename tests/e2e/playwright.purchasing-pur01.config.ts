import { defineConfig, devices } from '@playwright/test'

/**
 * Purchasing suite `pur01` (`browser:pur01:ci`) on the disposable normal-profile stack.
 * Every runtime value is supplied by browser-acceptance.yml; there is no local fallback.
 * Serial and retry-free because one test owns one seeded order and its idempotent approval attempt.
 */
function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the Purchasing pur01 suite runs only inside the disposable stack.`)
  }
  return value
}

const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: './purchasing-pur01/specs',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: `${artifactsDir}/purchasing-pur01-playwright-output`,
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

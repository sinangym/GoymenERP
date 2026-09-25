import { defineConfig, devices } from '@playwright/test'

/**
 * BROWSER-CI plumbing smoke (`browser:smoke:ci`).
 *
 * WHAT THIS IS: proof that the selectable browser workflow
 * (`.github/workflows/browser-acceptance.yml`) brings up the disposable C-01 stack
 * (SQL + API + authorized/unauthorized web profiles), materializes the canonical fixture and
 * drives a real Chromium against the real API. WHAT THIS IS NOT: acceptance evidence for any
 * feature. Feature suites (c03, org03, u02) bring their own specs and configs.
 *
 * The workflow's suite contract, which every suite config honours:
 *   BROWSER_E2E_AUTHORIZED_BASE_URL / BROWSER_E2E_UNAUTHORIZED_BASE_URL — the two web profiles
 *   BROWSER_E2E_API_BASE_URL — the disposable API
 *   BROWSER_E2E_ARTIFACTS_DIR — where outputs go (privacy-checked before upload)
 *   BROWSER_E2E_JUNIT_FILE — the ONE JUnit file the workflow reads for the clean-run check
 * No defaults: a run without the stack must fail, never test something else.
 */

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the browser smoke runs only inside the disposable stack.`)
  }
  return value
}

const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: './browser-ci',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  outputDir: `${artifactsDir}/browser-smoke-playwright-output`,
  reporter: [['list'], ['junit', { outputFile: required('BROWSER_E2E_JUNIT_FILE') }]],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: required('BROWSER_E2E_AUTHORIZED_BASE_URL'),
    viewport: { width: 1400, height: 900 },
    // Same artifact-privacy decision as the C-01 and SAB configs: a trace captures full request
    // URLs and CI uploads artifacts.
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})

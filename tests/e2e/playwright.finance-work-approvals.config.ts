import { defineConfig, devices } from '@playwright/test'

/**
 * One exact FIN01 run for the existing Finance journeys and D-05 Work inbox.
 *
 * The workflow materializes only the reviewed FIN01 fixture. Its closed seed is the source of both
 * suites' actor and record identities, so a second seed, profile or grant surface is not introduced.
 * Finance's existing spec/config and Claude's Work spec stay byte-for-byte owner artifacts.
 */
function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the combined Finance/Work suite runs only inside the disposable stack.`)
  }
  return value
}

const fin01Seed = required('BROWSER_E2E_FIN01_SEED')
const preboundWorkSeed = process.env.BROWSER_E2E_WORK_APPROVALS_SEED?.trim()
if (preboundWorkSeed && preboundWorkSeed !== fin01Seed) {
  throw new Error('BROWSER_E2E_WORK_APPROVALS_SEED must be the exact FIN01 fixture export.')
}
process.env.BROWSER_E2E_WORK_APPROVALS_SEED = fin01Seed

const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: '.',
  testMatch: [
    /finance-fin01[\\/]specs[\\/].*\.spec\.ts$/,
    /work-approvals[\\/]specs[\\/]work-approvals\.spec\.ts$/,
  ],
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: `${artifactsDir}/finance-work-approvals-playwright-output`,
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

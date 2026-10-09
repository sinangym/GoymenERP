import { defineConfig, devices } from '@playwright/test'

/**
 * Suite `fin01` (`browser:fin01:ci`) — the aggregate of the D-05 Work approval acceptance and the UNCHANGED Finance
 * fin01 specs, on the same disposable stack and the same `BROWSER_E2E_FIN01_SEED` (#648 fixture). No new suite,
 * workflow option or .github change: the fin01 workflow run executes both.
 *
 * Two projects, run serially in this order with one worker:
 *  1. `work-approvals` — read only (it approves nothing); first, so it sees the seed exactly as written (one Pending
 *     Sales draft, 51 Pending Finance batches) before any Finance spec uploads/approves its own batch;
 *  2. `finance-fin01` — the existing Finance specs with the settings of playwright.finance-fin01.config.ts (unchanged
 *     and still runnable on its own). No project depends on the other: a Work failure never skips the Finance specs.
 *
 * Retries 0 and forbidOnly; traces, screenshots and video off (the artifact-privacy decision).
 */

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the fin01 suite runs only inside the disposable stack.`)
  }
  return value
}

const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')
const baseURL = required('BROWSER_E2E_AUTHORIZED_BASE_URL')

export default defineConfig({
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: `${artifactsDir}/finance-fin01-playwright-output`,
  reporter: [['list'], ['junit', { outputFile: required('BROWSER_E2E_JUNIT_FILE') }]],
  projects: [
    {
      name: 'work-approvals',
      testDir: './work-approvals/specs',
      testMatch: /(^|[\\/])work-approvals\.spec\.ts$/,
      use: {
        ...devices['Desktop Chrome'],
        browserName: 'chromium',
        baseURL,
        viewport: { width: 1440, height: 900 },
        locale: 'tr-TR',
        trace: 'off',
        screenshot: 'off',
        video: 'off',
      },
    },
    {
      name: 'finance-fin01',
      testDir: './finance-fin01/specs',
      use: {
        ...devices['Desktop Chrome'],
        browserName: 'chromium',
        baseURL,
        viewport: { width: 1400, height: 900 },
        trace: 'off',
        screenshot: 'off',
        video: 'off',
      },
    },
  ],
})

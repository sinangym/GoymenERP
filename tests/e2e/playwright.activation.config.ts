import { defineConfig, devices } from '@playwright/test'

/**
 * C-01A activation evidence — the history/revoke scenario of #184 §2.1.
 *
 * Its own config, its own JUnit. The C-01 evidence contract requires exactly ten
 * `B-C01-*` testcases and the SAB contract requires exactly its four, so this
 * suite must not join either report.
 *
 * `fullyParallel` is off and `workers` is 1 because this case MUTATES a shared
 * grant in the disposable database. Two of these running at once would revoke the
 * grant out from under each other and both would pass for the wrong reason.
 *
 * No retries. A run that only succeeds on the second attempt has not demonstrated
 * the behaviour, and #184 disqualifies a series containing a retry outright.
 */
const baseURL = process.env.ACTIVATION_E2E_BASE_URL?.trim()
if (!baseURL) {
  throw new Error(
    'ACTIVATION_E2E_BASE_URL is not set. This suite revokes a real grant in a ' +
      'disposable database; it has no default so it can never be aimed elsewhere.',
  )
}

export default defineConfig({
  testDir: './activation',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 40_000 },
  outputDir: `${process.env.ACTIVATION_E2E_ARTIFACTS_DIR ?? 'artifacts'}/activation-output`,
  reporter: [
    ['list'],
    [
      'junit',
      {
        outputFile: `${process.env.ACTIVATION_E2E_ARTIFACTS_DIR ?? 'artifacts'}/activation-junit.xml`,
      },
    ],
  ],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL,
    viewport: { width: 1400, height: 900 },
    // Same artifact-privacy decision as the other two suites: a trace captures
    // full request URLs and CI uploads artifacts.
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})

import { defineConfig, devices } from '@playwright/test'
import { readEnv } from './src/contract.js'

/**
 * ONE project, on purpose.
 *
 * The evidence contract requires the JUnit report to carry exactly ten testcase
 * entries whose B-C01 ids are unique. Running the viewport matrix as Playwright
 * projects would emit five entries per case and fail that check, so the reflow
 * matrix is walked INSIDE B-C01-009 instead. See `EXPECTED_VIEWPORTS`.
 *
 * No retries. A C-01 acceptance case that only passes on the second attempt has
 * not demonstrated the behaviour it claims; `retry-200` is a fault the suite
 * injects deliberately, which is a different thing from a flaky test.
 */
const env = readEnv()

export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  outputDir: `${env.artifactsDir}/c01-e2e-playwright-output`,
  reporter: [
    ['list'],
    ['junit', { outputFile: `${env.artifactsDir}/c01-e2e-junit.xml` }],
    ['html', { outputFolder: `${env.artifactsDir}/c01-e2e-html-report`, open: 'never' }],
    ['./src/evidenceReporter.ts'],
  ],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: env.authorizedBaseUrl,
    viewport: { width: 1600, height: 900 },
    ignoreHTTPSErrors: false,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
})

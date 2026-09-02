import { defineConfig, devices } from '@playwright/test'

/**
 * SHELL-AUTH-BOUNDARY-001 (Issue #165) browser acceptance.
 *
 * SEPARATE FROM THE C-01 CONFIG ON PURPOSE. The C-01 evidence contract requires
 * the JUnit report to carry exactly ten `B-C01-*` testcases; adding SAB cases to
 * that run would break it. These have their own testDir, their own JUnit file and
 * no evidence reporter.
 *
 * WHAT THIS SUITE IS FOR. Issue #165's checklist has twelve items. Eight of them
 * (SAB-001..005, 010, 011) are decided in the reducer and are covered by the
 * committed vitest suites, which can drive the real provider directly. The four
 * here cannot be proved in jsdom at all:
 *
 *   SAB-006  refetch parity          — needs a real refetch trigger
 *   SAB-007  route/back-forward/focus— needs a real history and a real focus event
 *   SAB-008  stale actor A 401 → B   — needs a response DELIVERED after the switch
 *   SAB-009  stale company A 403 → B — same, for capability rather than session
 *
 * The distinction that makes this suite necessary: in jsdom the superseded request
 * is ABORTED, so nothing is delivered and nothing is rejected. Only response-stage
 * interception can hold a real response, let the context move, and then deliver it.
 */

const authorizedBaseUrl = process.env.SAB_E2E_BASE_URL?.trim()
if (!authorizedBaseUrl) {
  throw new Error(
    'SAB_E2E_BASE_URL is not set. This suite runs against a build that contains the ' +
      'SHELL-AUTH-BOUNDARY changes; it has no default so it can never silently test ' +
      'a shell that does not have them.',
  )
}

export default defineConfig({
  testDir: './sab',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  outputDir: `${process.env.SAB_E2E_ARTIFACTS_DIR ?? 'artifacts'}/sab-playwright-output`,
  reporter: [
    ['list'],
    [
      'junit',
      { outputFile: `${process.env.SAB_E2E_ARTIFACTS_DIR ?? 'artifacts'}/sab-junit.xml` },
    ],
  ],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: authorizedBaseUrl,
    viewport: { width: 1400, height: 900 },
    // Same artifact-privacy decision as the C-01 config: a trace captures full
    // request URLs and CI uploads artifacts.
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})

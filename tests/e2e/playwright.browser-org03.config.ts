import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

/**
 * ORG-03 real-browser acceptance in the selectable BROWSER-CI workflow (`browser:org03:ci`).
 *
 * WHAT IT RUNS: `organization-locations/specs/o-04-work-center-journey.spec.ts` — the work-center
 * list, the location card from a row, the capability and scope cases, and the company switch —
 * against the disposable stack the workflow built from the exact head: real API, real SQL with
 * the seeded organization data, the authorized web profile. Nothing is mocked; the journey
 * asserts it issues no write of any kind.
 *
 * THE CONTRACT (tests/e2e/browser-ci/README.md), no defaults: a run outside the stack must fail,
 * never test something else. The Organization tuple is built from the same variables
 * (`organization-locations/src/browserCi.ts`); the actors are bound by BROWSER-CI stage 2 in
 * `BROWSER_E2E_ORG03_ACTORS`, and without them the run refuses.
 *
 * Retries 0 and forbidOnly: a case that passes on its second attempt has not shown the
 * behaviour. Traces, screenshots and video are off (the C-01 artifact-privacy decision); only
 * the JUnit file is uploaded, after the privacy check.
 */
function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the org03 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

const here = path.dirname(fileURLToPath(import.meta.url))
const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')
// The journey's own evidence (coverage, run identity) stays inside the artifact root; the
// workflow uploads only its allowlist from there.
process.env.ORG_EVIDENCE_DIR = path.join(artifactsDir, 'org03-evidence')

export default defineConfig({
  globalSetup: path.join(here, 'organization-locations', 'global-setup.ts'),
  testDir: path.join(here, 'organization-locations', 'specs'),
  testMatch: /(^|[\\/])o-04-work-center-journey\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(artifactsDir, 'browser-org03-playwright-output'),
  reporter: [['list'], ['junit', { outputFile: required('BROWSER_E2E_JUNIT_FILE') }]],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: required('BROWSER_E2E_AUTHORIZED_BASE_URL'),
    viewport: { width: 1280, height: 800 },
    locale: 'tr-TR',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})

import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

/**
 * Organization suite `org03` (`browser:org03:ci`) — ORG-03 work-centre list acceptance in the selectable
 * BROWSER-CI workflow: a real browser against the disposable stack built from the exact head — real API,
 * real SQL, the suite's capability profile (`normal`, where the Organization capabilities are published),
 * the authorized web.
 *
 * The read cases write nothing and read the built-in organization seed, so the suite needs no fixture and
 * no seed envelope. The ORG master-edit cases (ORG-ME-*) WRITE sites, locations and work centres through the
 * real screens as the disposable org03-editor; org03 resolves on the workflow's writing arm.
 *
 * The cases are kept serial (`fullyParallel: false`, `workers: 1`) and IN FILE ORDER: the read cases count
 * the five seeded work centres exactly, so the writing cases are declared last and must stay last.
 *
 * The contract (tests/e2e/browser-ci/README.md), no defaults. Retries 0 and forbidOnly; traces,
 * screenshots and video off (the C-01 artifact-privacy decision).
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

export default defineConfig({
  testDir: path.join(here, 'organization-org03', 'specs'),
  testMatch: /(^|[\\/])org03-work-centre-list\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(artifactsDir, 'organization-org03-playwright-output'),
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

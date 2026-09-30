import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

/**
 * Cari suite `c02` (`browser:c02:ci`) — the NORMAL-runtime create/reopen journey in the selectable
 * BROWSER-CI workflow: a real browser against the disposable stack built from the exact head — real
 * API, real SQL, the `normal` capability profile, the authorized web.
 *
 * WHY A SEPARATE IDENTITY FROM `c03`. Suite ids name the journey's PRIMARY screen (`s04`, `acc01`,
 * `org03`), and this journey's primary is C-02; `c03` is the read-only, multi-company detail journey.
 * C-02 writes a new Party/account and therefore cannot share c03's read-only SQL invariant.
 *
 * IT WRITES, and that is the point: C-02 creates a counterparty through the real API, so the journey
 * is screen → API → persisted row → reopen. It therefore resolves on the workflow's writing arm and
 * is absent from the validator's read-only set — correctly, since the post-browser SQL verification
 * asserts zero audit and command deltas, which a create must violate.
 *
 * Serial (`fullyParallel: false`, `workers: 1`): the cases share one disposable API, one of them
 * depends on the record an earlier one created, and two sign in as different actors. A failure then
 * names one case rather than a race.
 *
 * The contract (tests/e2e/browser-ci/README.md), no defaults. Retries 0 and forbidOnly; traces,
 * screenshots and video off (the C-01 artifact-privacy decision).
 */
function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the c02 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

const here = path.dirname(fileURLToPath(import.meta.url))
const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: path.join(here, 'cari-c02', 'specs'),
  // The create/reopen journey, the AT-065 document journey on a card of its own, then the AT-004 read-only access view,
  // then the AT-009 VKN/TCKN journey on its own cards with maker and checker actors (alphabetical, serial; each file
  // creates what it reads, and the access view reads nothing the others write).
  testMatch: /(^|[\\/])c02-(create-and-reopen|documents|identity-access-view|tax-identity)\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(artifactsDir, 'cari-c02-playwright-output'),
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

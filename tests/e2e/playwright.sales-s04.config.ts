import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

/**
 * Sales suite `s04` (`browser:s04:ci`) — SALES-A1 user-flow acceptance in the selectable BROWSER-CI
 * workflow: the internal approval axis (S-04 submit / approve / reject, S-02 state filter) in a real
 * browser against the disposable stack built from the exact head — real API, real SQL, the `normal`
 * capability profile (the Sales screens are published to whoever holds the grant), the authorized web.
 * S05 continues the same serial record with the price/discount revision form, reopen, archived approval
 * history and terminal withdrawal replay in `specs/s04-commercial-revision.spec.ts`.
 *
 * It WRITES: every transition is a real command through the screens on the ONE draft the stage-2 seed
 * wrote (`BROWSER_E2E_S04_SEED`, sales-s04/src/env.ts), and every step is re-read through the real API.
 * The cases are one serial journey on that draft; `fullyParallel: false`, `workers: 1`, file order.
 *
 * SALES-A2a (specs/s04-references.spec.ts, after the journey): the seed also opens a REAL Cari party with a
 * REAL delivery address and contact through the real Cari API, and writes a SECOND draft through the gated
 * writer carrying those references; the browser proves the REAL options of the new-draft selects and the
 * reopen of the stored references. The create WITH references is proven on real SQL, not here, until the
 * product picker (E1) exists. The seed's central rows are CARİ's: Program.cs handler-factory arguments and
 * the ISalesPartyContactInformationPortV1 registration; SUITE_SEED_FIELDS["s04"] +8 in the validator.
 *
 * DEC-SAL-003 (specs/s04-delivery-schedule.spec.ts, after the D2 persona file): the disposable planner, the ONLY holder
 * of sales.orders.delivery-schedule.write in the run database (user decision 2026-09-27), moves the delivery dates of a
 * separate seeded order after it is submitted and approved through the real API; replay, stale version and the 403 of
 * every other s04 actor are proven through the real API and read-only SQL.
 *
 * DEC-SAL-005 / AT-007 (specs/s04-cancellation.spec.ts): the preparer creates and submits, the approver approves a separate
 * order through the real API; the disposable canceller (the ONLY holder of sales.orders.cancel in the run database, a central
 * override delta) previews its impact from the real owners and cancels it in the browser; replay, stale version, the next
 * module's refusal and the 403 of every other s04 actor are proven through the real API and read-only SQL.
 *
 * The contract (tests/e2e/browser-ci/README.md), no defaults. Retries 0 and forbidOnly; traces,
 * screenshots and video off (the C-01 artifact-privacy decision).
 */
function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the s04 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

const here = path.dirname(fileURLToPath(import.meta.url))
const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: path.join(here, 'sales-s04', 'specs'),
  // SALES-A2a/S05: references and commercial revision remain in the one serial exact-head journey.
  // DEC-SAL-003: the delivery-schedule journey runs on its own seeded order (file order: after d2, before references).
  // DEC-SAL-005: the cancellation journey runs on its own order created through the real API (file order: after the approval
  // journey); its disposable canceller is a central override delta (see the spec header).
  // DEC-SAL-002: the quantity-revision journey runs on its own order created through the real API (file order: after the
  // delivery schedule, before references); the preparer's quantity grant is a central override delta (see the spec header).
  // INV-AL-1 change (specs/s04-approved-allocation-change.spec.ts): file order puts it right after the approval journey
  // (the draft is Approved and S04-06 has released its allocation); it reuses the preparer's existing grants.
  // AT-002 (specs/s04-global-search.spec.ts): read-only global search with the preparer and the reader; it only reopens
  // the never-mutated delivery draft and the REAL Cari party, so file order does not matter; no actor or grant is added.
  testMatch: /(^|[\\/])s04-(approval-journey|approved-allocation-change|cancellation|commercial-revision|customer-acceptance|d2-persona-withdrawal|delivery-schedule|draft-list-ds01|global-search|party-display|price-list|quantity-revision|references|remainder-changes)\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(artifactsDir, 'sales-s04-playwright-output'),
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

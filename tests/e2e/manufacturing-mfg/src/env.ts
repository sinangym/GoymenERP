/**
 * The `mfg` suite's inputs, from the BROWSER-CI workflow's contract (`tests/e2e/browser-ci/README.md`)
 * plus the suite's own seed binding. Every value is required; there is no default and no fallback:
 * a run outside the disposable stack fails and never tests something else.
 *
 * `BROWSER_E2E_MFG_SEED` (JSON) is written by the stage-2 seed step (a2 part 2, CARİ's interface)
 * together with its synthetic test-database grants. This suite adds no actor, role or grant; it
 * only names what it needs:
 *
 *   recorderActor — Development user holding, company-wide in `companyId`, the five Manufacturing
 *                   permissions (plans.write, plans.approve, orders.create, facts.record, orders.read)
 *   readerActor   — Development user holding only manufacturing.orders.read there
 *   companyId     — the company whose approved kilogram profile the real Settings provider answers
 *   workCenterId  — one of that company's work centers
 *   orderId       — an open order the fixture (tests/GoymenERP.BrowserSuiteFixtureTool/Suites/MFG)
 *                   arranged through the real API; the reader case reads it
 *   rollId        — that order's parent roll
 *
 * The screen's capabilities (`manufacturing.orders.detail` / `.facts`) must be published by the
 * stack's capability profile (MFG-UI card D4, `mfg-staged`), since the provider is staged in the
 * normal runtime.
 */

export interface MfgSeed {
  readonly recorderActor: string
  readonly readerActor: string
  readonly companyId: string
  readonly workCenterId: string
  readonly orderId: string
  readonly rollId: string
}

export interface MfgEnvironment {
  readonly apiBaseUrl: string
  readonly testedCommit: string
  readonly seed: MfgSeed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the mfg suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

function text(raw: Record<string, unknown>, field: keyof MfgSeed, guid = false): string {
  const value = raw[field]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value.trim()))) {
    throw new Error(`BROWSER_E2E_MFG_SEED field "${field}" is missing or malformed`)
  }
  return value.trim()
}

export function readEnvironment(): MfgEnvironment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) {
    throw new Error(`BROWSER_E2E_TESTED_COMMIT must be a full 40-character commit, got "${testedCommit}"`)
  }
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }
  const raw = JSON.parse(required('BROWSER_E2E_MFG_SEED')) as Record<string, unknown>
  const seed: MfgSeed = {
    recorderActor: text(raw, 'recorderActor'),
    readerActor: text(raw, 'readerActor'),
    companyId: text(raw, 'companyId', true),
    workCenterId: text(raw, 'workCenterId', true),
    orderId: text(raw, 'orderId', true),
    rollId: text(raw, 'rollId', true),
  }
  if (seed.recorderActor === seed.readerActor) {
    throw new Error('BROWSER_E2E_MFG_SEED binds the same actor as recorder and reader')
  }
  return { apiBaseUrl, testedCommit, seed }
}

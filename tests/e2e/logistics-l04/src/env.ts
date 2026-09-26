/**
 * The `l04` suite's inputs, from the BROWSER-CI workflow's contract (`tests/e2e/browser-ci/README.md`)
 * plus the suite's own seed binding. Every value is required; there is no default and no fallback: a run
 * outside the disposable stack fails and never tests something else.
 *
 * `BROWSER_E2E_L04_SEED` (JSON) is written by the stage-2 seed step from
 * `tests/GoymenERP.BrowserSuiteFixtureTool/Suites/L04`, next to the synthetic grants of
 * `infra/browser-suites/l04.override.yml`. This suite adds no actor, role or grant; it only names what it
 * needs:
 *
 *   preparerActor    — Development user holding logistics.loading-instructions.create + .read and
 *                      sales.drafts.read (read-only, to pick an approved line) company-wide in `companyId`
 *   loaderActor      — Development user holding logistics.loads.record + logistics.loading-instructions.read
 *   readerActor      — Development user holding only logistics.loading-instructions.read
 *   companyId        — the GDC company the web profile's shell is committed to
 *   otherCompanyId   — the other seeded company (GSP), where none of the actors holds a Logistics grant
 *   customerPartyId / companyAccountId — the ONE Cari customer the seed opened through the real Cari API
 *   draftId / approvedLineId — the ONE APPROVED Sales draft (one kg line) the seed wrote through the gated
 *                      Sales fixture writer and moved through the real Sales API; the positive path picks
 *                      this line IN THE UI by its visible label, never by these ids
 *   instructionId    — the ONE Issued instruction (two lines on that approved line) the seed created through
 *                      the real Logistics API; `blockedLineId` is its line the gated Logistics fixture writer
 *                      blocked (the only state no API produces in 4a), `openLineId` its other line
 */
export interface L04Seed {
  readonly preparerActor: string
  readonly loaderActor: string
  readonly readerActor: string
  readonly companyId: string
  readonly otherCompanyId: string
  readonly customerPartyId: string
  readonly companyAccountId: string
  readonly draftId: string
  readonly approvedLineId: string
  readonly instructionId: string
  readonly blockedLineId: string
  readonly openLineId: string
  readonly shipmentAllocationId: string
  readonly shipmentLabelId: string
}

export interface L04Environment {
  readonly apiBaseUrl: string
  readonly sqlConnection: string
  readonly testedCommit: string
  readonly seed: L04Seed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the l04 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

function text(raw: Record<string, unknown>, field: keyof L04Seed, guid = false): string {
  const value = raw[field]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value.trim()))) {
    throw new Error(`BROWSER_E2E_L04_SEED field "${field}" is missing or malformed`)
  }
  return value.trim()
}

export function readEnvironment(): L04Environment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) {
    throw new Error(`BROWSER_E2E_TESTED_COMMIT must be a full 40-character commit, got "${testedCommit}"`)
  }
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }
  const raw = JSON.parse(required('BROWSER_E2E_L04_SEED')) as Record<string, unknown>
  const seed: L04Seed = {
    preparerActor: text(raw, 'preparerActor'),
    loaderActor: text(raw, 'loaderActor'),
    readerActor: text(raw, 'readerActor'),
    companyId: text(raw, 'companyId', true),
    otherCompanyId: text(raw, 'otherCompanyId', true),
    customerPartyId: text(raw, 'customerPartyId', true),
    companyAccountId: text(raw, 'companyAccountId', true),
    draftId: text(raw, 'draftId', true),
    approvedLineId: text(raw, 'approvedLineId', true),
    instructionId: text(raw, 'instructionId', true),
    blockedLineId: text(raw, 'blockedLineId', true),
    openLineId: text(raw, 'openLineId', true),
    shipmentAllocationId: text(raw, 'shipmentAllocationId', true),
    shipmentLabelId: text(raw, 'shipmentLabelId', true),
  }
  if (new Set([seed.preparerActor, seed.loaderActor, seed.readerActor]).size !== 3) {
    throw new Error('BROWSER_E2E_L04_SEED binds the same actor to two roles')
  }
  if (seed.companyId === seed.otherCompanyId || seed.blockedLineId === seed.openLineId) {
    throw new Error('BROWSER_E2E_L04_SEED names the same company or line twice')
  }
  return { apiBaseUrl, sqlConnection: required('BROWSER_E2E_SQL_CONNECTION'), testedCommit, seed }
}

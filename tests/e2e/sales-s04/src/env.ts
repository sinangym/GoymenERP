/**
 * The `s04` suite's inputs, from the BROWSER-CI workflow's contract (`tests/e2e/browser-ci/README.md`)
 * plus the suite's own seed binding. Every value is required; there is no default and no fallback: a run
 * outside the disposable stack fails and never tests something else.
 *
 * `BROWSER_E2E_S04_SEED` (JSON) is written by the stage-2 seed step (a2 part 2, CARİ's interface) from
 * `tests/GoymenERP.BrowserSuiteFixtureTool/Suites/S04`, next to the synthetic grants of
 * `infra/browser-suites/s04.override.yml`. This suite adds no actor, role or grant; it only names what it
 * needs:
 *
 *   preparerActor        — Development user holding sales.drafts.create + sales.drafts.read company-wide in `companyId`
 *   approverActor        — Development user holding sales.orders.approve + sales.drafts.read there
 *   readerActor          — Development user holding only sales.drafts.read there
 *   companyId            — the GDC company the web profile's shell is committed to
 *   draftId              — the ONE Preparing draft with two lines the seed wrote through the gated Sales fixture
 *                          writer (synthetic owner snapshots; no attempt, no audit); every transition the suite
 *                          makes on it goes through the real API + UI
 *   deliveryDraftId      — SALES-A2a: the SECOND draft the gated writer wrote on the REAL Cari party below, carrying
 *                          the REAL delivery address and contact (ids + the owner's versions); reopened, never mutated
 *   realPartyId          — the REAL Cari party the seed opened through the real counterparty endpoint
 *   realCompanyAccountId — its company account in `companyId`
 *   deliveryAddressId    — the REAL delivery address written through the real contact-information command
 *   deliveryContactId    — the REAL contact person written the same way
 */

export interface S04Seed {
  readonly preparerActor: string
  readonly approverActor: string
  readonly readerActor: string
  readonly supportCreatorActor: string
  readonly ordinaryCreatorActor: string
  readonly companyId: string
  readonly draftId: string
  readonly supportDraftId: string
  readonly ordinaryApproveDraftId: string
  readonly ordinaryRejectDraftId: string
  readonly withdrawalDraftId: string
  readonly deliveryDraftId: string
  readonly realPartyId: string
  readonly realCompanyAccountId: string
  readonly deliveryAddressId: string
  readonly deliveryContactId: string
}

export interface S04Environment {
  readonly apiBaseUrl: string
  readonly testedCommit: string
  readonly seed: S04Seed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the s04 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

function text(raw: Record<string, unknown>, field: keyof S04Seed, guid = false): string {
  const value = raw[field]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value.trim()))) {
    throw new Error(`BROWSER_E2E_S04_SEED field "${field}" is missing or malformed`)
  }
  return value.trim()
}

export function readEnvironment(): S04Environment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) {
    throw new Error(`BROWSER_E2E_TESTED_COMMIT must be a full 40-character commit, got "${testedCommit}"`)
  }
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }
  const raw = JSON.parse(required('BROWSER_E2E_S04_SEED')) as Record<string, unknown>
  const seed: S04Seed = {
    preparerActor: text(raw, 'preparerActor'),
    approverActor: text(raw, 'approverActor'),
    readerActor: text(raw, 'readerActor'),
    supportCreatorActor: text(raw, 'supportCreatorActor'),
    ordinaryCreatorActor: text(raw, 'ordinaryCreatorActor'),
    companyId: text(raw, 'companyId', true),
    draftId: text(raw, 'draftId', true),
    supportDraftId: text(raw, 'supportDraftId', true),
    ordinaryApproveDraftId: text(raw, 'ordinaryApproveDraftId', true),
    ordinaryRejectDraftId: text(raw, 'ordinaryRejectDraftId', true),
    withdrawalDraftId: text(raw, 'withdrawalDraftId', true),
    deliveryDraftId: text(raw, 'deliveryDraftId', true),
    realPartyId: text(raw, 'realPartyId', true),
    realCompanyAccountId: text(raw, 'realCompanyAccountId', true),
    deliveryAddressId: text(raw, 'deliveryAddressId', true),
    deliveryContactId: text(raw, 'deliveryContactId', true),
  }
  if (new Set([seed.preparerActor, seed.approverActor, seed.readerActor, seed.supportCreatorActor, seed.ordinaryCreatorActor]).size !== 5) {
    throw new Error('BROWSER_E2E_S04_SEED binds the same actor to two personas')
  }
  if (new Set([
    seed.draftId,
    seed.deliveryDraftId,
    seed.supportDraftId,
    seed.ordinaryApproveDraftId,
    seed.ordinaryRejectDraftId,
    seed.withdrawalDraftId,
  ]).size !== 6) {
    throw new Error('BROWSER_E2E_S04_SEED repeats a draft id')
  }
  return { apiBaseUrl, testedCommit, seed }
}

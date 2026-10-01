/**
 * The `acc01` suite's inputs, from the BROWSER-CI workflow's contract plus the suite's own seed binding.
 * Every value is required; there is no default and no fallback: a run outside the disposable stack fails
 * and never tests something else.
 *
 * `BROWSER_E2E_ACC01_SEED` (JSON) is written by the stage-2 seed step from
 * tests/GoymenERP.BrowserSuiteFixtureTool/Suites/ACC01. Its ONE write is Accounting's fixed SENTETIK posted journal
 * (with the fixed fin01 Open September period it posts into), through the owner's gated fixture port — the journal
 * ACC01-09 opens; no Accounting HTTP route can post one. It then reads the period and journal lists through the real
 * host as each actor and fails on anything else. The journal is read back by the suite from the real API, so it
 * travels as no seed output:
 *
 *   companyId      — GDC, where both actors hold their Accounting grants
 *   companyBId     — GSP, where the preparer may read periods only (company isolation)
 *   preparerActor  — Development user who proposes; holds the approve grant too (Ö4 must still refuse it)
 *   approverActor  — another Development user, who approves (Ö4: never the preparer) and closes (Ö5)
 *
 * The Accounting capabilities come from the stack's capability profile: the provider is STAGED on the normal
 * host until this suite is green, so the run needs the disposable staged Accounting profile (central).
 */

export interface Acc01Seed {
  readonly companyId: string
  readonly companyBId: string
  readonly preparerActor: string
  readonly approverActor: string
  readonly normalSourceLabel: string
  readonly normalOccurredOn: string
  readonly normalAmount: string
  readonly normalDebitLabel: string
  readonly normalCreditLabel: string
  readonly openPeriodId: string
}

export interface Acc01Environment {
  readonly apiBaseUrl: string
  readonly unauthorizedBaseUrl: string
  readonly seed: Acc01Seed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the acc01 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

function text(raw: Record<string, unknown>, field: keyof Acc01Seed, guid = false): string {
  const value = raw[field]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value.trim()))) {
    throw new Error(`BROWSER_E2E_ACC01_SEED field "${field}" is missing or malformed`)
  }
  return value.trim()
}

export function readEnvironment(): Acc01Environment {
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }
  const raw = JSON.parse(required('BROWSER_E2E_ACC01_SEED')) as Record<string, unknown>
  const seed: Acc01Seed = {
    companyId: text(raw, 'companyId', true),
    companyBId: text(raw, 'companyBId', true),
    preparerActor: text(raw, 'preparerActor'),
    approverActor: text(raw, 'approverActor'),
    normalSourceLabel: text(raw, 'normalSourceLabel'),
    normalOccurredOn: text(raw, 'normalOccurredOn'),
    normalAmount: text(raw, 'normalAmount'),
    normalDebitLabel: text(raw, 'normalDebitLabel'),
    normalCreditLabel: text(raw, 'normalCreditLabel'),
    openPeriodId: text(raw, 'openPeriodId', true),
  }
  if (seed.preparerActor === seed.approverActor) {
    throw new Error('BROWSER_E2E_ACC01_SEED binds the same actor as preparer and approver (Ö4)')
  }
  if (seed.companyId === seed.companyBId) {
    throw new Error('BROWSER_E2E_ACC01_SEED binds the same company twice')
  }
  return { apiBaseUrl, unauthorizedBaseUrl: required('BROWSER_E2E_UNAUTHORIZED_BASE_URL'), seed }
}

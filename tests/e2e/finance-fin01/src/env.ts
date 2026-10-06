/**
 * The `fin01` suite's inputs, from the BROWSER-CI workflow's contract plus the suite's own seed
 * binding. Every value is required; there is no default and no fallback: a run outside the
 * disposable stack fails and never tests something else.
 *
 * `BROWSER_E2E_FIN01_SEED` (JSON) is written by the stage-2 seed step from
 * tests/GoymenERP.BrowserSuiteFixtureTool/Suites/FIN01 (the real counterparty API only):
 *
 *   companyId        — GDC, whose approved TRY money profile the real Settings provider answers
 *   partyId          — the synthetic customer the seed opened through the real Cari API
 *   companyAccountId — its company account in GDC
 *   proposerActor    — Development user proposing the account and preparing the draft
 *   approverActor    — another Development user, who approves (Ö4: never the proposer)
 *   readerActor      — allocation.read-only Development user; never receives allocation.write
 *
 * The Finance capabilities come from the NORMAL host: the provider is published since FIN-UI-1c, and a
 * screen opens only for an actor holding its own Finance permission (the suite override's grants).
 */

export interface Fin01Seed {
  readonly companyId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly proposerActor: string
  readonly approverActor: string
  readonly readerActor: string
}

export interface Fin01Environment {
  readonly unauthorizedBaseUrl: string
  readonly seed: Fin01Seed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the fin01 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

function text(raw: Record<string, unknown>, field: keyof Fin01Seed, guid = false): string {
  const value = raw[field]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value.trim()))) {
    throw new Error(`BROWSER_E2E_FIN01_SEED field "${field}" is missing or malformed`)
  }
  return value.trim()
}

export function readEnvironment(): Fin01Environment {
  const raw = JSON.parse(required('BROWSER_E2E_FIN01_SEED')) as Record<string, unknown>
  const seed: Fin01Seed = {
    companyId: text(raw, 'companyId', true),
    partyId: text(raw, 'partyId', true),
    companyAccountId: text(raw, 'companyAccountId', true),
    proposerActor: text(raw, 'proposerActor'),
    approverActor: text(raw, 'approverActor'),
      readerActor: text(raw, 'readerActor'),
  }
  if (new Set([seed.proposerActor, seed.approverActor, seed.readerActor]).size !== 3) {
    throw new Error('BROWSER_E2E_FIN01_SEED must bind distinct proposer, approver and reader actors')
  }
  return { unauthorizedBaseUrl: required('BROWSER_E2E_UNAUTHORIZED_BASE_URL'), seed }
}

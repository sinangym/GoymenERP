/**
 * The Work approval acceptance inputs (D-05 Sales → Onaylarım), run inside the `fin01` suite (`browser:fin01:ci`
 * aggregates the unchanged Finance specs and this Work spec). Every value is required; there is no default and no
 * fallback: a run outside the disposable stack fails and never tests something else.
 *
 * `BROWSER_E2E_FIN01_SEED` (JSON) is written by the stage-2 seed step from
 * tests/GoymenERP.BrowserSuiteFixtureTool/Suites/FIN01 (#648; owned by the fixture owner, not this suite). The fields
 * read here:
 *   companyId        — GDC, the committed company of the web profile
 *   otherCompanyId   — GSP, where only the Finance approver holds an (empty-queue) Finance approval role
 *   financeOnlyActor — Finance official-invoice approver (also the GSP role); no Sales approve
 *   salesOnlyActor   — Sales approver (sales.drafts.read + sales.orders.approve); no Finance approve
 *   bothActor        — both native approval permissions in GDC
 *   neitherActor     — neither approval permission
 *   salesDraftId     — the one real Sales draft in PendingApproval (submitted by the Sales preparer)
 *   financeBatchId   — one of the 51 real Pending Finance batches (uploaded by the proposer); 51 > the page of 50,
 *                      so the Work continuation is a real cursor
 */

export interface WorkApprovalsSeed {
  readonly companyId: string
  readonly otherCompanyId: string
  readonly financeOnlyActor: string
  readonly salesOnlyActor: string
  readonly bothActor: string
  readonly neitherActor: string
  readonly salesDraftId: string
  readonly financeBatchId: string
}

export interface WorkApprovalsEnvironment {
  readonly apiBaseUrl: string
  readonly testedCommit: string
  readonly seed: WorkApprovalsSeed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; the Work approval acceptance runs only inside the fin01 disposable stack.`)
  return value
}

function text(raw: Record<string, unknown>, name: keyof WorkApprovalsSeed, guid = false): string {
  const value = raw[name]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value))) {
    throw new Error(`BROWSER_E2E_FIN01_SEED field "${name}" is missing or malformed`)
  }
  return value
}

export function readEnvironment(): WorkApprovalsEnvironment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) {
    throw new Error(`BROWSER_E2E_TESTED_COMMIT must be a full 40-character commit, got "${testedCommit}"`)
  }
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }
  const raw = JSON.parse(required('BROWSER_E2E_FIN01_SEED')) as Record<string, unknown>
  const seed: WorkApprovalsSeed = {
    companyId: text(raw, 'companyId', true),
    otherCompanyId: text(raw, 'otherCompanyId', true),
    financeOnlyActor: text(raw, 'financeOnlyActor'),
    salesOnlyActor: text(raw, 'salesOnlyActor'),
    bothActor: text(raw, 'bothActor'),
    neitherActor: text(raw, 'neitherActor'),
    salesDraftId: text(raw, 'salesDraftId', true),
    financeBatchId: text(raw, 'financeBatchId', true),
  }
  if (new Set([seed.financeOnlyActor, seed.salesOnlyActor, seed.bothActor, seed.neitherActor]).size !== 4) {
    throw new Error('BROWSER_E2E_FIN01_SEED binds the same actor to two Work personas')
  }
  if (seed.companyId.toLowerCase() === seed.otherCompanyId.toLowerCase()) {
    throw new Error('BROWSER_E2E_FIN01_SEED names the same company twice')
  }
  return { apiBaseUrl, testedCommit, seed }
}

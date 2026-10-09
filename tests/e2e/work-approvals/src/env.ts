/**
 * The `work-approvals` acceptance inputs (D-05 Sales → Onaylarım). Every value is required; there is no default and
 * no fallback: a run outside an isolated acceptance stack fails and never tests something else.
 *
 * `BROWSER_E2E_WORK_APPROVALS_SEED` (JSON) is provided by the acceptance environment's actual owner (seed/grants are
 * NOT part of this source lease). It names, in ONE company:
 *   financeOnlyActor — holds finance.official-invoice-import.approve only (no Sales approve)
 *   salesOnlyActor   — holds sales.orders.approve + sales.drafts.read (no Finance approve)
 *   bothActor        — holds both owners' approve capabilities
 *   neitherActor     — holds the Sales read only (no approve of either owner)
 *   companyId        — the company the web profile's shell is committed to
 *   salesDraftId     — a Sales order draft in PendingApproval, submitted by ANOTHER actor
 *   financeBatchId   — a Finance official-invoice-import batch in Pending, uploaded by ANOTHER actor
 */

export interface WorkApprovalsSeed {
  readonly financeOnlyActor: string
  readonly salesOnlyActor: string
  readonly bothActor: string
  readonly neitherActor: string
  readonly companyId: string
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
  if (!value) throw new Error(`${name} is not set; the work-approvals suite runs only inside an isolated acceptance stack.`)
  return value
}

function text(raw: Record<string, unknown>, name: keyof WorkApprovalsSeed, guid = false): string {
  const value = raw[name]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value))) {
    throw new Error(`BROWSER_E2E_WORK_APPROVALS_SEED field "${name}" is missing or malformed`)
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
  const raw = JSON.parse(required('BROWSER_E2E_WORK_APPROVALS_SEED')) as Record<string, unknown>
  const seed: WorkApprovalsSeed = {
    financeOnlyActor: text(raw, 'financeOnlyActor'),
    salesOnlyActor: text(raw, 'salesOnlyActor'),
    bothActor: text(raw, 'bothActor'),
    neitherActor: text(raw, 'neitherActor'),
    companyId: text(raw, 'companyId', true),
    salesDraftId: text(raw, 'salesDraftId', true),
    financeBatchId: text(raw, 'financeBatchId', true),
  }
  if (new Set([seed.financeOnlyActor, seed.salesOnlyActor, seed.bothActor, seed.neitherActor]).size !== 4) {
    throw new Error('BROWSER_E2E_WORK_APPROVALS_SEED binds the same actor to two personas')
  }
  return { apiBaseUrl, testedCommit, seed }
}

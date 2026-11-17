import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeInventoryCost, fetchInventoryCosts, sendInventoryCost, type InventoryCostAttempt } from './inventoryCostApi'
import { COMPANY_A, COMPANY_B, PREPARER, APPROVER, ACCOUNT_DEBIT, ACCOUNT_CREDIT, PERIOD_SEPTEMBER, json } from './testHarness'

const id = 'c0633000-0000-4000-8000-000000000001'
const line = 'c0633000-0000-4000-8000-000000000002'
const source = 'c0633000-0000-4000-8000-000000000003'
const fixture = (patch: Record<string, unknown> = {}) => ({ proposalId: id, companyId: COMPANY_A, eventId: source, kind: 1,
  invoiceId: source, invoiceVersion: 3, admissionId: source, invoiceDate: '2026-09-17', officialInvoiceNumber: 'SENTETIK-2026-1',
  component: { componentKey: `invoice-line:${line}`, productId: source, invoiceLineId: line, invoiceLineNumber: 1,
    locationId: null, signedTlAmount: { numerator: '10001', denominator: '100' }, sourceProofSha256: 'a'.repeat(64) },
  version: 2, status: 'Prepared', sourceOccurredAtUtc: '2026-09-17T09:00:00Z', exactTlAmount: '100.01',
  preparation: { periodId: PERIOD_SEPTEMBER, accountingDate: '2026-09-17', transactionDate: '2026-09-17', currencyId: source,
    signedAmount: '100.01', moneyProfileId: source, moneyProfileVersion: 2, moneyScale: 2, preparedBy: PREPARER,
    preparedAtUtc: '2026-09-17T09:01:00Z', reason: 'SENTETIK actual owner cost',
    classification: { classification: 1, accountId: ACCOUNT_DEBIT, origin: 'Product', productVersion: '3', classificationVersion: '2', mappingVersion: 1, invoiceLineRevision: 0 },
    counterAccount: { accountId: ACCOUNT_CREDIT, code: '153', name: 'SENTETIK counter' }, accountPlanPackageId: source,
    accountPlanPackageVersion: 2, accountPlanCatalogVersion: 1, accountPlanSourceVersion: 'SENTETIK-plan', accountPlanSha256: 'b'.repeat(64) },
  approvedBy: null, approvedAtUtc: null, journalId: null, cancellationId: null, reversalProposal: null, ...patch })

afterEach(() => vi.unstubAllGlobals())
describe('immutable inventory cost proposal contract', () => {
  it('keeps exact source text and refuses foreign, incomplete, and self-approved responses', () => {
    expect(decodeInventoryCost(fixture(), COMPANY_A, 1)?.exactTlAmount).toBe('100.01')
    expect(decodeInventoryCost(fixture({ companyId: COMPANY_B }), COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost(fixture({ kind: 2 }), COMPANY_A, 1)).toBeNull()
    const value = fixture()
    expect(decodeInventoryCost(fixture({ preparation: { ...value.preparation, moneyProfileId: null } }), COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost(fixture({ status: 'Posted', version: 3, approvedBy: PREPARER, approvedAtUtc: '2026-09-17T09:02:00Z', journalId: source }), COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost(fixture({ status: 'NoCost', version: 1, preparation: null }), COMPANY_A, 1)).toBeNull()
  })
  it('treats a changed line or missing actor in a successful command response as unknown', async () => {
    const attempt: InventoryCostAttempt = { id, kind: 1, key: 'same-key', actor: APPROVER, operation: 'approve', request: { expectedVersion: 2 } }
    vi.stubGlobal('fetch', vi.fn(async () => json(fixture({ status: 'Posted', version: 3, approvedBy: PREPARER, approvedAtUtc: '2026-09-17T09:02:00Z', journalId: source }))))
    expect(await sendInventoryCost(COMPANY_A, attempt)).toEqual({ type: 'unknown' })
    vi.stubGlobal('fetch', vi.fn(async () => json({ status: 'Posted' })))
    expect(await sendInventoryCost(COMPANY_A, attempt)).toEqual({ type: 'unknown' })
  })
  it('retries an unknown result with the same immutable key and body without converting a 503 to rejection', async () => {
    const attempt: InventoryCostAttempt = { id, kind: 1, key: 'same-key', actor: APPROVER, operation: 'approve', request: { expectedVersion: 2 } }
    const calls: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init: RequestInit) => { calls.push(init); return calls.length === 1
      ? new Response('{}', { status: 503 }) : json(fixture({ status: 'Posted', version: 3, approvedBy: APPROVER, approvedAtUtc: '2026-09-17T09:02:00Z', journalId: source })) }))
    expect(await sendInventoryCost(COMPANY_A, attempt)).toEqual({ type: 'unknown' })
    expect((await sendInventoryCost(COMPANY_A, attempt)).type).toBe('applied')
    expect(calls[0].headers).toEqual(calls[1].headers); expect(calls[0].body).toBe(calls[1].body)
  })
  it('shows malformed rather than an empty list for duplicate or foreign proposals', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: COMPANY_A, items: [fixture(), fixture()] })))
    expect(await fetchInventoryCosts(COMPANY_A, 1)).toEqual({ type: 'malformed' })
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: COMPANY_A, items: [fixture({ companyId: COMPANY_B })] })))
    expect(await fetchInventoryCosts(COMPANY_A, 1)).toEqual({ type: 'malformed' })
  })
})

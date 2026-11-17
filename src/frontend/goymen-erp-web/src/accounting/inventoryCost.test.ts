import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeInventoryCost, fetchInventoryCosts, sendInventoryCost, type InventoryCostAttempt } from './inventoryCostApi'
import { COMPANY_A, COMPANY_B, PREPARER, APPROVER, ACCOUNT_DEBIT, ACCOUNT_CREDIT, PERIOD_SEPTEMBER,
  JOURNAL, PROPOSAL, json, reversalProposal, approvedReversalProposal } from './testHarness'

const id = 'c0633000-0000-4000-8000-000000000001'
const line = 'c0633000-0000-4000-8000-000000000002'
const source = 'c0633000-0000-4000-8000-000000000003'
const correctedJournal = 'c0633000-0000-4000-8000-000000000004'
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
const cancelled = (patch: Record<string, unknown> = {}) => fixture({ status: 'CancellationReversalPending', version: 3,
  approvedBy: APPROVER, approvedAtUtc: '2026-09-17T09:02:00Z', journalId: JOURNAL,
  currentPostedJournalId: correctedJournal, cancellationId: source,
  reversalProposal: reversalProposal({ sourceJournalId: correctedJournal }), ...patch })

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
  it.each(['999999.01', 'SENTETIK invalid amount', '100.010', '+100.01', '01.01', '1e2', '-0'])(
    'refuses a contradictory or noncanonical prepared display amount %s without browser calculations', exactTlAmount => {
      expect(decodeInventoryCost(fixture({ exactTlAmount }), COMPANY_A, 1)).toBeNull()
    })
  it('keeps canonical exact ratio displays and requires the explicit zero display for a no-cost source', () => {
    const value = fixture()
    const ratio = fixture({ status: 'AwaitingPreparation', version: 1, preparation: null, exactTlAmount: '1 / 3',
      component: { ...value.component, signedTlAmount: { numerator: '1', denominator: '3' } } })
    expect(decodeInventoryCost(ratio, COMPANY_A, 1)?.exactTlAmount).toBe('1 / 3')
    expect(decodeInventoryCost({ ...ratio, exactTlAmount: '2 / 3' }, COMPANY_A, 1)).toBeNull()
    const zero = { ...ratio, status: 'NoCost', exactTlAmount: '0',
      component: { ...value.component, signedTlAmount: { numerator: '0', denominator: '1' } } }
    expect(decodeInventoryCost(zero, COMPANY_A, 1)?.exactTlAmount).toBe('0')
    expect(decodeInventoryCost({ ...zero, exactTlAmount: '1' }, COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost({ ...zero, exactTlAmount: '0 / 1' }, COMPANY_A, 1)).toBeNull()
    const negative = fixture({ kind: 2, exactTlAmount: '-100.01',
      component: { ...value.component, componentKey: `product-location:${source}:${line}`, invoiceLineId: null,
        invoiceLineNumber: null, locationId: line, signedTlAmount: { numerator: '-10001', denominator: '100' } },
      preparation: { ...value.preparation, signedAmount: '-100.01' } })
    expect(decodeInventoryCost(negative, COMPANY_A, 2)?.exactTlAmount).toBe('-100.01')
  })
  it('does not report approval applied when the returned display contradicts the frozen prepared amount', async () => {
    const attempt: InventoryCostAttempt = { id, kind: 1, key: 'SENTETIK-amount-key', actor: APPROVER,
      operation: 'approve', request: { expectedVersion: 2 } }
    vi.stubGlobal('fetch', vi.fn(async () => json(fixture({ status: 'Posted', version: 3, approvedBy: APPROVER,
      approvedAtUtc: '2026-09-17T09:02:00Z', journalId: JOURNAL, exactTlAmount: '999999.01' }))))
    expect(await sendInventoryCost(COMPANY_A, attempt)).toEqual({ type: 'unknown' })
  })
  it('keeps legacy reads without a reversal and binds a reversal to the actual corrected journal rather than the original', () => {
    expect(decodeInventoryCost(fixture(), COMPANY_A, 1)?.currentPostedJournalId).toBeNull()
    const value = decodeInventoryCost(cancelled(), COMPANY_A, 1)
    expect(value?.journalId).toBe(JOURNAL)
    expect(value?.currentPostedJournalId).toBe(correctedJournal)
    expect(value?.reversalProposal?.sourceJournalId).toBe(correctedJournal)
    expect(decodeInventoryCost(cancelled({ currentPostedJournalId: null }), COMPANY_A, 1)).toBeNull()
    const { currentPostedJournalId: _omitted, ...legacyReversal } = cancelled()
    expect(decodeInventoryCost(legacyReversal, COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost(cancelled({ reversalProposal: reversalProposal({ sourceJournalId: source }) }), COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost(cancelled({ reversalProposal: reversalProposal({ sourceJournalId: JOURNAL }) }), COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost(cancelled({ status: 'Posted' }), COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost(fixture({ currentPostedJournalId: correctedJournal }), COMPANY_A, 1)).toBeNull()
    expect(decodeInventoryCost(cancelled({ status: 'CancelledByReversal',
      reversalProposal: approvedReversalProposal({ sourceJournalId: correctedJournal }) }), COMPANY_A, 1)?.status).toBe('CancelledByReversal')
  })
  it('refuses a crossed reversal source from a list and keeps a mismatched successful reversal command unknown', async () => {
    const attempt: InventoryCostAttempt = { id, kind: 1, key: 'SENTETIK-reversal-key', actor: PREPARER, operation: 'reverse',
      request: { expectedVersion: 3, proposalId: PROPOSAL, periodId: PERIOD_SEPTEMBER,
        accountingDate: '2026-09-24', transactionDate: '2026-09-23', reason: 'SENTETIK ters kayıt gerekçesi' } }
    const crossed = cancelled({ reversalProposal: reversalProposal({ sourceJournalId: source }) })
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: COMPANY_A, items: [crossed] })))
    expect(await fetchInventoryCosts(COMPANY_A, 1)).toEqual({ type: 'malformed' })
    vi.stubGlobal('fetch', vi.fn(async () => json(crossed)))
    expect(await sendInventoryCost(COMPANY_A, attempt)).toEqual({ type: 'unknown' })
    vi.stubGlobal('fetch', vi.fn(async () => json(cancelled())))
    expect((await sendInventoryCost(COMPANY_A, attempt)).type).toBe('applied')
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

import { describe, expect, it } from 'vitest'
import {
  decodeAppliedAllocationOpenItemBatch,
  decodeApprovedAllocationOpenItemSourcePage,
  type ApprovedAllocationOpenItemSourceBatch,
  type ApprovedAllocationOpenItemSourceRow,
} from './allocationSelectionContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const BATCH = '22222222-2222-4222-8222-222222222222'
const ITEM = '33333333-3333-4333-8333-333333333333'
const DOCUMENT = '44444444-4444-4444-8444-444444444444'
const INSTALLMENT = '55555555-5555-4555-8555-555555555555'
const PARTY = '66666666-6666-4666-8666-666666666666'
const ACCOUNT = '77777777-7777-4777-8777-777777777777'
const CURRENCY = '88888888-8888-4888-8888-888888888888'
const CHECKSUM = 'a'.repeat(64)

const row: ApprovedAllocationOpenItemSourceRow = {
  openItemId: ITEM,
  sourceDocumentId: DOCUMENT,
  sourceInstallmentId: INSTALLMENT,
  sourceVersion: 'invoice-v1',
  documentKind: 'official-invoice-installment',
  partyId: PARTY,
  companyAccountId: ACCOUNT,
  currencyId: CURRENCY,
  amount: '432.10',
  dueOn: '2026-10-31',
}

const batch: ApprovedAllocationOpenItemSourceBatch = {
  batchId: BATCH,
  companyId: COMPANY,
  batchVersion: 'batch-v1',
  batchChecksum: CHECKSUM,
  rows: [row],
}

function applied(overrides: Record<string, unknown> = {}) {
  return {
    batchId: BATCH,
    companyId: COMPANY,
    batchVersion: batch.batchVersion,
    batchChecksum: CHECKSUM,
    openItems: [{
      id: ITEM,
      companyId: COMPANY,
      sourceKind: 'official.invoice-installment.v1',
      sourceDocumentId: DOCUMENT,
      sourceInstallmentId: INSTALLMENT,
      sourceVersion: 'invoice-v1',
      sourceBatchId: BATCH,
      sourceBatchVersion: batch.batchVersion,
      sourceBatchChecksum: CHECKSUM,
      partyId: PARTY,
      companyAccountId: ACCOUNT,
      currencyId: CURRENCY,
      originalAmount: '432.10',
      outstandingAmount: '432.10',
      dueOn: '2026-10-31',
      status: 1,
      version: '1',
      ...overrides,
    }],
  }
}

describe('allocationSelectionContract', () => {
  it('decodes the exact approved catalog and exact applied open item graph', () => {
    expect(decodeApprovedAllocationOpenItemSourcePage({ companyId: COMPANY, items: [batch], nextCursor: null }, COMPANY))
      .toEqual({ ok: true, value: { companyId: COMPANY, items: [batch], nextCursor: null } })

    expect(decodeAppliedAllocationOpenItemBatch(applied(), COMPANY, batch)).toMatchObject({
      ok: true,
      value: { batchId: BATCH, openItems: [{ id: ITEM, status: 1, version: '1' }] },
    })
  })

  it('rejects extra fields, foreign companies, duplicate identities and changed batch bindings', () => {
    expect(decodeApprovedAllocationOpenItemSourcePage({ companyId: COMPANY, items: [batch], nextCursor: null, extra: true }, COMPANY).ok)
      .toBe(false)
    expect(decodeApprovedAllocationOpenItemSourcePage({ companyId: COMPANY, items: [{ ...batch, companyId: PARTY }], nextCursor: null }, COMPANY).ok)
      .toBe(false)
    expect(decodeApprovedAllocationOpenItemSourcePage({ companyId: COMPANY, items: [batch, batch], nextCursor: null }, COMPANY).ok)
      .toBe(false)
    expect(decodeAppliedAllocationOpenItemBatch({
      batchId: BATCH,
      companyId: COMPANY,
      batchVersion: 'changed',
      batchChecksum: CHECKSUM,
      openItems: [],
    }, COMPANY, batch).ok).toBe(false)
  })

  it('rejects owner-invalid catalog rows and ambiguous immutable identities', () => {
    expect(decodeApprovedAllocationOpenItemSourcePage({
      companyId: COMPANY, items: [{ ...batch, rows: [] }], nextCursor: null,
    }, COMPANY).ok).toBe(false)
    expect(decodeApprovedAllocationOpenItemSourcePage({
      companyId: COMPANY,
      items: [{ ...batch, rows: [row, { ...row, openItemId: PARTY }] }],
      nextCursor: null,
    }, COMPANY).ok).toBe(false)
    expect(decodeApprovedAllocationOpenItemSourcePage({
      companyId: COMPANY,
      items: [{ ...batch, rows: [{ ...row, dueOn: '2026-02-30' }] }],
      nextCursor: null,
    }, COMPANY).ok).toBe(false)
    expect(decodeApprovedAllocationOpenItemSourcePage({
      companyId: COMPANY,
      items: [{ ...batch, rows: [{ ...row, sourceVersion: 'v'.repeat(129) }] }],
      nextCursor: null,
    }, COMPANY).ok).toBe(false)
    for (const sourceVersion of [' invoice-v1', 'invoice v1', 'invoice@v1', 'fatura-ş1']) {
      expect(decodeApprovedAllocationOpenItemSourcePage({
        companyId: COMPANY,
        items: [{ ...batch, rows: [{ ...row, sourceVersion }] }],
        nextCursor: null,
      }, COMPANY).ok).toBe(false)
    }
    for (const batchVersion of [' batch-v1', 'batch v1', 'batch@v1', 'sürüm-1']) {
      expect(decodeApprovedAllocationOpenItemSourcePage({
        companyId: COMPANY, items: [{ ...batch, batchVersion }], nextCursor: null,
      }, COMPANY).ok).toBe(false)
    }
  })

  it('binds every applied row to the catalog and rejects impossible mutable state', () => {
    expect(decodeAppliedAllocationOpenItemBatch({ ...applied(), openItems: [] }, COMPANY, batch).ok).toBe(false)
    expect(decodeAppliedAllocationOpenItemBatch({
      ...applied(), openItems: [...applied().openItems, ...applied().openItems],
    }, COMPANY, batch).ok).toBe(false)
    expect(decodeAppliedAllocationOpenItemBatch(applied({ sourceDocumentId: PARTY }), COMPANY, batch).ok).toBe(false)
    expect(decodeAppliedAllocationOpenItemBatch(applied({ originalAmount: '432.11' }), COMPANY, batch).ok).toBe(false)
    expect(decodeAppliedAllocationOpenItemBatch(applied({ dueOn: '2026-02-30' }), COMPANY, batch).ok).toBe(false)
    expect(decodeAppliedAllocationOpenItemBatch(applied({ version: '9223372036854775808' }), COMPANY, batch).ok).toBe(false)
    expect(decodeAppliedAllocationOpenItemBatch(applied({ outstandingAmount: '500.00' }), COMPANY, batch).ok).toBe(false)
    expect(decodeAppliedAllocationOpenItemBatch(applied({ status: 2, outstandingAmount: '1.00' }), COMPANY, batch).ok).toBe(false)
  })
})

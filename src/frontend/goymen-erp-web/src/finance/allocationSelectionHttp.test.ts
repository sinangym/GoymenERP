import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  approvedAllocationSourcesUrl,
  readAllocationReceiptDetail,
  readAllocationReceiptSources,
  readAppliedAllocationOpenItems,
  readApprovedAllocationSources,
} from './allocationSelectionHttp'
import type { ApprovedAllocationOpenItemSourceBatch } from './allocationSelectionContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const BATCH = '22222222-2222-4222-8222-222222222222'
const ITEM = '33333333-3333-4333-8333-333333333333'
const DOCUMENT = '44444444-4444-4444-8444-444444444444'
const INSTALLMENT = '55555555-5555-4555-8555-555555555555'
const PARTY = '66666666-6666-4666-8666-666666666666'
const ACCOUNT = '77777777-7777-4777-8777-777777777777'
const CURRENCY = '88888888-8888-4888-8888-888888888888'
const binding = { companyId: COMPANY, actorId: 'finance-user', sessionEpoch: 3 }
const batch: ApprovedAllocationOpenItemSourceBatch = {
  batchId: BATCH,
  companyId: COMPANY,
  batchVersion: 'batch-v1',
  batchChecksum: 'a'.repeat(64),
  rows: [{
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
  }],
}

const applied = {
  batchId: BATCH,
  companyId: COMPANY,
  batchVersion: batch.batchVersion,
  batchChecksum: batch.batchChecksum,
  openItems: [{
    id: ITEM,
    companyId: COMPANY,
    sourceKind: 'official.invoice-installment.v1',
    sourceDocumentId: DOCUMENT,
    sourceInstallmentId: INSTALLMENT,
    sourceVersion: 'invoice-v1',
    sourceBatchId: BATCH,
    sourceBatchVersion: batch.batchVersion,
    sourceBatchChecksum: batch.batchChecksum,
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    currencyId: CURRENCY,
    originalAmount: '432.10',
    outstandingAmount: '432.10',
    dueOn: '2026-10-31',
    status: 1,
    version: '1',
  }],
}

afterEach(() => vi.unstubAllGlobals())

describe('allocationSelectionHttp', () => {
  it('preserves receipt-list and detail authorization statuses for the current auth boundary', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 403 }))
    vi.stubGlobal('fetch', fetchMock)
    await expect(readAllocationReceiptSources(binding, null)).resolves.toEqual({ kind: 'authorization', status: 401 })
    await expect(readAllocationReceiptDetail(binding, ITEM)).resolves.toEqual({ kind: 'authorization', status: 403 })
  })

  it('reads the approved catalog with the bound company and encoded cursor', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      companyId: COMPANY,
      items: [batch],
      nextCursor: null,
    }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const result = await readApprovedAllocationSources(binding, 'next+/=')
    expect(result).toMatchObject({ kind: 'found', resource: { companyId: COMPANY, items: [{ batchId: BATCH }] } })
    expect(fetchMock).toHaveBeenCalledWith(approvedAllocationSourcesUrl('next+/='), expect.objectContaining({
      method: 'GET',
      headers: expect.objectContaining({ 'X-Company-Id': COMPANY }),
    }))
  })

  it('binds exact-batch reopen and fails closed for authorization or malformed bodies', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 403 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(applied), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...batch, openItems: [], extra: true }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    await expect(readAppliedAllocationOpenItems(binding, batch)).resolves.toEqual({ kind: 'authorization', status: 403 })
    await expect(readAppliedAllocationOpenItems(binding, batch)).resolves.toMatchObject({
      kind: 'found', resource: { batchId: BATCH, openItems: [{ id: ITEM, status: 1 }] },
    })
    await expect(readAppliedAllocationOpenItems(binding, batch)).resolves.toEqual({ kind: 'malformed' })
  })
})

import { describe, expect, it } from 'vitest'
import { decodeApprovalReply, decodePendingDetail, decodePendingPage } from './officialInvoiceImportApprovalContract'

const batch = {
  batchId: '11111111-1111-4111-8111-111111111111', companyId: '22222222-2222-4222-8222-222222222222',
  batchVersion: 'invoice-v1', batchChecksum: 'a'.repeat(64), templateVersion: 'finance-official-invoice-installment-csv.v1',
  status: 'Pending', version: '1', rowCount: 1, uploadedBy: 'maker', uploadedAtUtc: '2026-09-30T12:00:00Z',
  approvedBy: null, approvedAtUtc: null,
} as const
const row = {
  openItemId: '33333333-3333-4333-8333-333333333333', sourceDocumentId: '44444444-4444-4444-8444-444444444444',
  sourceInstallmentId: '55555555-5555-4555-8555-555555555555', sourceVersion: 'invoice-v1',
  documentKind: 'official-invoice-installment', partyId: '66666666-6666-4666-8666-666666666666',
  companyAccountId: '77777777-7777-4777-8777-777777777777', currencyId: '88888888-8888-4888-8888-888888888888',
  amount: '1250.50', dueOn: '2026-10-31',
} as const
const detail = { batch, preview: { templateVersion: batch.templateVersion, sourceSha256: batch.batchChecksum,
  batch: { batchId: batch.batchId, companyId: batch.companyId, batchVersion: batch.batchVersion,
    batchChecksum: batch.batchChecksum, rows: [row] }, issues: [] } } as const

describe('official invoice pending approval contract', () => {
  it('decodes the complete company page and immutable detail', () => {
    expect(decodePendingPage({ companyId: batch.companyId, items: [batch], nextCursor: null }).ok).toBe(true)
    expect(decodePendingDetail(detail)).toEqual({ ok: true, value: detail })
  })

  it('refuses unknown members, foreign company items and tuple drift', () => {
    expect(decodePendingPage({ companyId: batch.companyId, items: [{ ...batch, hidden: true }], nextCursor: null })).toEqual({ ok: false })
    expect(decodePendingPage({ companyId: '99999999-9999-4999-8999-999999999999', items: [batch], nextCursor: null })).toEqual({ ok: false })
    expect(decodePendingDetail({ ...detail, preview: { ...detail.preview, sourceSha256: 'b'.repeat(64) } })).toEqual({ ok: false })
  })

  it('accepts only a complete approved command resource', () => {
    const approved = { ...batch, status: 'Approved', version: '2', approvedBy: 'checker', approvedAtUtc: '2026-09-30T13:00:00Z' }
    expect(decodeApprovalReply({ outcome: 'Succeeded', resource: approved, isReplay: false,
      originalCorrelationId: 'corr-1', issues: [] })).toEqual({
      ok: true, value: { kind: 'succeeded', replayed: false, batch: approved },
    })
    expect(decodeApprovalReply({ outcome: 'Succeeded', resource: { ...approved, approvedBy: null }, isReplay: false,
      originalCorrelationId: 'corr-1', issues: [] })).toEqual({ ok: false })
  })
})

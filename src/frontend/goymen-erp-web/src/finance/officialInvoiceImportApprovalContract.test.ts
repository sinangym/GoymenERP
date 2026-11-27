import { describe, expect, it } from 'vitest'
import { decodeApprovalReply, decodeDecisionDetail, decodePendingDetail, decodePendingPage, decodeUploadReply } from './officialInvoiceImportApprovalContract'

const batch = {
  batchId: '11111111-1111-4111-8111-111111111111', companyId: '10000000-0000-0000-0000-000000000001',
  batchVersion: 'invoice-v1', batchChecksum: 'a'.repeat(64), templateVersion: 'finance-official-invoice-installment-csv.v1',
  status: 'Pending', version: '1', rowCount: 1, uploadedBy: 'maker', uploadedAtUtc: '2026-09-30T12:00:00Z',
  approvedBy: null, approvedAtUtc: null,
} as const
const row = {
  openItemId: '33333333-3333-4333-8333-333333333333', sourceDocumentId: '44444444-4444-4444-8444-444444444444',
  sourceInstallmentId: '55555555-5555-4555-8555-555555555555', sourceVersion: 'invoice-v1',
  documentKind: 'official-invoice-installment', partyId: '66666666-6666-4666-8666-666666666666',
  companyAccountId: '77777777-7777-4777-8777-777777777777', currencyId: '22222222-2222-2222-2222-222222222222',
  amount: '1250.50', dueOn: '2026-10-31',
} as const
const detail = { batch, preview: { templateVersion: batch.templateVersion, sourceSha256: batch.batchChecksum, isValid: true,
  batch: { batchId: batch.batchId, companyId: batch.companyId, batchVersion: batch.batchVersion,
    batchChecksum: batch.batchChecksum, rows: [row] }, issues: [] } } as const

describe('official invoice pending approval contract', () => {
  it('decodes the complete company page and immutable detail', () => {
    expect(decodePendingPage({ companyId: batch.companyId, items: [batch], nextCursor: null }).ok).toBe(true)
    expect(decodePendingDetail(detail)).toEqual({ ok: true, value: detail })
  })

  it('decodes the FNS-D64 tax warnings of a pending preview and refuses a malformed warning', () => {
    const warning = { code: 'finance_official_invoice_tax_inconsistent', sourceDocumentId: row.sourceDocumentId,
      expectedPayableFunctionalAmount: '1200', payableFunctionalAmount: '1200.01', differenceFunctionalAmount: '0.01' }
    const withWarnings = (taxWarnings: unknown) => ({ ...detail, preview: { ...detail.preview, taxWarnings } })
    expect(decodePendingDetail(withWarnings([warning]))).toEqual({ ok: true, value: withWarnings([warning]) })
    expect(decodePendingDetail(withWarnings(null)).ok).toBe(true)
    const { sourceDocumentId: _omitted, ...missingKey } = warning
    expect(decodePendingDetail(withWarnings([{ ...warning, hidden: true }]))).toEqual({ ok: false })
    expect(decodePendingDetail(withWarnings([missingKey]))).toEqual({ ok: false })
    expect(decodePendingDetail(withWarnings([{ ...warning, differenceFunctionalAmount: 0.01 }]))).toEqual({ ok: false })
    expect(decodePendingDetail(withWarnings(warning))).toEqual({ ok: false })
    expect(decodePendingDetail({ ...detail, preview: { ...detail.preview, hidden: true } })).toEqual({ ok: false })
  })

  it('refuses unknown members, foreign company items and tuple drift', () => {
    expect(decodePendingPage({ companyId: batch.companyId, items: [{ ...batch, hidden: true }], nextCursor: null })).toEqual({ ok: false })
    expect(decodePendingPage({ companyId: '99999999-9999-4999-8999-999999999999', items: [batch], nextCursor: null })).toEqual({ ok: false })
    expect(decodePendingPage({ companyId: '00000000-0000-0000-0000-000000000000', items: [], nextCursor: null })).toEqual({ ok: false })
    expect(decodePendingDetail({ ...detail, preview: { ...detail.preview, batch: { ...detail.preview.batch,
      rows: [{ ...row, currencyId: 'not-a-guid' }] } } })).toEqual({ ok: false })
    expect(decodePendingDetail({ ...detail, preview: { ...detail.preview, sourceSha256: 'b'.repeat(64) } })).toEqual({ ok: false })
    expect(decodePendingDetail({ ...detail, preview: { ...detail.preview, isValid: false } })).toEqual({ ok: false })
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

  it('decodes only a complete pending upload command resource', () => {
    expect(decodeUploadReply({ outcome: 'Succeeded', resource: batch, isReplay: true,
      originalCorrelationId: 'corr-upload', issues: [] })).toEqual({
      ok: true, value: { kind: 'succeeded', replayed: true, batch },
    })
    expect(decodeUploadReply({ outcome: 'Succeeded', resource: { ...batch, status: 'Approved' }, isReplay: false,
      originalCorrelationId: 'corr-upload', issues: [] })).toEqual({ ok: false })
    expect(decodeUploadReply({ outcome: 'VersionConflict', resource: {}, isReplay: false,
      originalCorrelationId: 'corr-upload', issues: [] })).toEqual({ ok: false })
  })
})


describe('official invoice decision detail reuses the native preview without widening the legacy pending screen', () => {
  const withRow = (change: Record<string, unknown>) => ({ ...detail, preview: { ...detail.preview,
    batch: { ...detail.preview.batch, rows: [{ ...row, ...change }] } } })
  it('accepts Pending1 and Approved2, preserving the complete native header and row', () => {
    const terminal = { ...detail, batch: { ...batch, status: 'Approved', version: '2', approvedBy: 'checker', approvedAtUtc: '2026-09-30T13:00:00Z' } }
    expect(decodeDecisionDetail(detail)).toEqual({ ok: true, value: detail })
    expect(decodeDecisionDetail(terminal)).toEqual({ ok: true, value: terminal })
    expect(decodePendingDetail(terminal)).toEqual({ ok: false })
  })
  it.each(['0001250.5000', '99999999999999999999999999999999999999999999999999', '0.' + '0'.repeat(40) + '1'])('retains native exact positive digit text %s without CLR narrowing', amount => {
    const value = withRow({ amount })
    expect(decodeDecisionDetail(value)).toEqual({ ok: true, value })
  })
  it.each(['0','000.000','-1','1e3','1\n',' 1','1 ','1.','NaN'])('refuses a non-native positive amount %j', amount => {
    expect(decodeDecisionDetail(withRow({ amount }))).toEqual({ ok: false })
  })
  it('retains every row beyond the Work reference cap and the native 10000 row boundary', () => {
    const rows = Array.from({ length: 10_000 }, (_, i) => ({ ...row,
      openItemId: `33333333-3333-4333-8333-${String(i + 1).padStart(12, '0')}`,
      sourceInstallmentId: `55555555-5555-4555-8555-${String(i + 1).padStart(12, '0')}` }))
    const value = { ...detail, batch: { ...batch, rowCount: rows.length }, preview: { ...detail.preview,
      batch: { ...detail.preview.batch, rows } } }
    expect(decodeDecisionDetail(value)).toEqual({ ok: true, value })
    expect(decodeDecisionDetail({ ...value, batch: { ...value.batch, rowCount: 10_001 }, preview: { ...value.preview,
      batch: { ...value.preview.batch, rows: [...rows, { ...row, openItemId: '99999999-9999-4999-8999-999999999999' }] } } })).toEqual({ ok: false })
  })
  it('refuses duplicate tuples, invalid dates, hidden fields and mismatched header provenance', () => {
    for (const value of [withRow({ dueOn: '2026-02-30' }), withRow({ sourceVersion: 'bad\nversion' }), withRow({ hidden: true }),
      withRow({ documentKind: 'payment' }), { ...detail, batch: { ...batch, version: '2' } },
      { ...detail, batch: { ...batch, batchChecksum: batch.batchChecksum + '\n' } },
      { ...detail, preview: { ...detail.preview, batch: { ...detail.preview.batch, batchId: row.openItemId } } },
      { ...detail, batch: { ...batch, rowCount: 2 }, preview: { ...detail.preview, batch: { ...detail.preview.batch, rows: [row, row] } } }])
      expect(decodeDecisionDetail(value)).toEqual({ ok: false })
  })
  it('binds v2 tax warnings to their actual source rows and preserves their exact native values', () => {
    const templateVersion = 'finance-official-invoice-installment-csv.v2'
    const warning = { code: 'finance_official_invoice_tax_inconsistent', sourceDocumentId: row.sourceDocumentId,
      expectedPayableFunctionalAmount: '-0001200.00', payableFunctionalAmount: '1200.01', differenceFunctionalAmount: '0.01' }
    const value = { ...detail, batch: { ...batch, templateVersion }, preview: { ...detail.preview, templateVersion, taxWarnings: [warning] } }
    expect(decodeDecisionDetail(value)).toEqual({ ok: true, value })
    for (const change of [{ sourceDocumentId: null }, { sourceDocumentId: row.openItemId }, { code: 'made-up' },
      { differenceFunctionalAmount: '0' }, { expectedPayableFunctionalAmount: '1\n' }])
      expect(decodeDecisionDetail({ ...value, preview: { ...value.preview, taxWarnings: [{ ...warning, ...change }] } })).toEqual({ ok: false })
  })
})


it.each(['2026-02-30T08:00:00Z','2026-01-01T24:00:00Z','2026-01-01T23:60:00Z','0001-01-01T00:00:00Z','0001-01-01T00:00:00.0000000+00:00'])('refuses normalized or default native recorded timestamps %s',at=>{
 expect(decodeDecisionDetail({...detail,batch:{...batch,uploadedAtUtc:at}})).toEqual({ok:false})
 expect(decodeDecisionDetail({...detail,batch:{...batch,status:'Approved',version:'2',approvedBy:'checker',approvedAtUtc:at}})).toEqual({ok:false})
})
it('keeps native UTC fractional tick order and exact timestamp text',()=>{
 const uploadedAtUtc='2026-09-30T12:00:00.0000002+00:00',approvedAtUtc='2026-09-30T12:00:00.0000003Z'
 const value={...detail,batch:{...batch,uploadedAtUtc,status:'Approved',version:'2',approvedBy:'checker',approvedAtUtc}}
 expect(decodeDecisionDetail(value)).toEqual({ok:true,value})
 expect(decodeDecisionDetail({...value,batch:{...value.batch,approvedAtUtc:'2026-09-30T12:00:00.0000001Z'}})).toEqual({ok:false})
 const earliest={...detail,batch:{...batch,uploadedAtUtc:'0001-01-01T00:00:00.0000001Z'}}
 expect(decodeDecisionDetail(earliest)).toEqual({ok:true,value:earliest})
})

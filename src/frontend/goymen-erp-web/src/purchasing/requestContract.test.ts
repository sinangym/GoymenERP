import { describe, expect, it } from 'vitest'
import { decodePurchaseRequestCommandResponse, decodePurchaseRequestDetail, decodePurchaseRequestList } from './contract'

const companyId = '11111111-1111-4111-8111-111111111111'
const requestId = '22222222-2222-4222-8222-222222222222'
const lineId = '33333333-3333-4333-8333-333333333333'
const productId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'

const summary = {
  id: requestId,
  companyId,
  status: 'Draft',
  version: '2',
  effectiveOn: '2026-09-23',
  createdAtUtc: '2026-09-23T10:00:00Z',
  updatedAtUtc: '2026-09-23T10:00:00Z',
  lineCount: 1,
}
const line = { lineId, lineNo: 1, productId, productVersion: '7', unitId, quantity: '12.5' }

describe('purchase request wire contract', () => {
  it('decodes the strict list, detail and command envelopes', () => {
    expect(decodePurchaseRequestList({ items: [summary], nextCursor: null })?.items[0].id).toBe(requestId)
    expect(decodePurchaseRequestDetail({ request: summary, lines: [line] })?.lines[0].productVersion).toBe('7')
    expect(decodePurchaseRequestCommandResponse({
      detail: { request: summary, lines: [line] },
      isReplay: false,
      originalCorrelationId: 'corr-1',
    })?.detail.request.version).toBe('2')
  })

  it('rejects unknown fields, line-count drift and noncanonical versions', () => {
    expect(decodePurchaseRequestList({ items: [{ ...summary, unexpected: true }], nextCursor: null })).toBeNull()
    expect(decodePurchaseRequestDetail({ request: { ...summary, lineCount: 2 }, lines: [line] })).toBeNull()
    expect(decodePurchaseRequestDetail({ request: summary, lines: [{ ...line, productVersion: '07' }] })).toBeNull()
  })

  it('A1-2: decodes the line source and the summary source kinds and keeps a body before A1-2 readable', () => {
    const documentId = '66666666-6666-4666-8666-666666666666'
    const documentLineId = '77777777-7777-4777-8777-777777777777'
    const sales = { ...line, source: { kind: 'Sales', documentId, documentLineId } }
    const manual = { ...line, lineId: '88888888-8888-4888-8888-888888888888', lineNo: 2, source: null }
    const sourced = { ...summary, lineCount: 2, sourceKinds: ['Manual', 'Sales'] }
    const decoded = decodePurchaseRequestDetail({ request: sourced, lines: [sales, manual] })
    expect(decoded?.lines[0].source).toEqual({ kind: 'Sales', documentId, documentLineId })
    expect(decoded?.lines[1].source).toBeNull()
    expect(decoded?.request.sourceKinds).toEqual(['Manual', 'Sales'])
    expect(decodePurchaseRequestList({ items: [sourced], nextCursor: null })?.items[0].sourceKinds).toEqual(['Manual', 'Sales'])
    // Before A1-2: no field anywhere; the source is unknown (undefined), not manual.
    expect(decodePurchaseRequestDetail({ request: summary, lines: [line] })?.lines[0].source).toBeUndefined()
  })

  it('A1-2: rejects an unknown or manual line kind, an empty identity, loose fields and kinds that disagree with the lines', () => {
    const documentId = '66666666-6666-4666-8666-666666666666'
    const documentLineId = '77777777-7777-4777-8777-777777777777'
    const one = { ...summary, sourceKinds: ['Sales'] }
    const withSource = (source: unknown) => decodePurchaseRequestDetail({ request: one, lines: [{ ...line, source }] })
    expect(withSource({ kind: 'Sales', documentId, documentLineId })).not.toBeNull()
    expect(withSource({ kind: 'Manual', documentId, documentLineId })).toBeNull()
    expect(withSource({ kind: 'sales', documentId, documentLineId })).toBeNull()
    expect(withSource({ kind: 'Sales', documentId: '00000000-0000-0000-0000-000000000000', documentLineId })).toBeNull()
    expect(withSource({ kind: 'Sales', documentId })).toBeNull()
    expect(withSource({ kind: 'Sales', documentId, documentLineId, extra: 1 })).toBeNull()
    // The summary names exactly the lines' kinds, distinct and in the fixed order.
    expect(decodePurchaseRequestDetail({ request: { ...summary, sourceKinds: ['Manual'] }, lines: [{ ...line, source: { kind: 'Sales', documentId, documentLineId } }] })).toBeNull()
    expect(decodePurchaseRequestList({ items: [{ ...summary, lineCount: 2, sourceKinds: ['Sales', 'Manual'] }], nextCursor: null })).toBeNull()
    expect(decodePurchaseRequestList({ items: [{ ...summary, sourceKinds: ['Manual', 'Manual'] }], nextCursor: null })).toBeNull()
    expect(decodePurchaseRequestList({ items: [{ ...summary, sourceKinds: ['Purchasing'] }], nextCursor: null })).toBeNull()
    expect(decodePurchaseRequestList({ items: [{ ...summary, sourceKinds: [] }], nextCursor: null })).toBeNull()
    // A half-current body (kinds on the summary but not on the lines, or the reverse) is not trusted.
    expect(decodePurchaseRequestDetail({ request: { ...summary, sourceKinds: ['Manual'] }, lines: [line] })).toBeNull()
    expect(decodePurchaseRequestDetail({ request: summary, lines: [{ ...line, source: null }] })).toBeNull()
  })

  it('rejects zero request quantities', () => {
    expect(decodePurchaseRequestDetail({ request: summary, lines: [{ ...line, quantity: '0' }] })).toBeNull()
    expect(decodePurchaseRequestDetail({ request: summary, lines: [{ ...line, quantity: '0.0' }] })).toBeNull()
  })
})

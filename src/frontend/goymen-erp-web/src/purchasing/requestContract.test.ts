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
})

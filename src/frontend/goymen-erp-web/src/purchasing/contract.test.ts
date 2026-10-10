import { describe, expect, it } from 'vitest'
import { decodePurchaseOrderCommandResponse, decodePurchaseOrderDetail, decodePurchaseOrderList, supplierDisplayOf } from './contract'

const COMPANY_GDC = '10000000-0000-0000-0000-000000000001'
/** The legacy `PurchaseOrderSummaryView`: what create/approve command responses carry. */
const commandSummary = {
  id: '11111111-1111-4111-8111-111111111111', companyId: COMPANY_GDC,
  status: 'Draft', version: '1', supplierPartyId: '33333333-3333-4333-8333-333333333333',
  companyAccountId: '44444444-4444-4444-8444-444444444444', sourceRequestId: null,
  effectiveOn: '2026-09-22', createdAtUtc: '2026-09-22T10:00:00Z', updatedAtUtc: '2026-09-22T10:00:00Z', lineCount: 1,
}
const supplierDisplay = {
  partyId: commandSummary.supplierPartyId, code: 'CAR-000042',
  legalName: 'Çağlayan Öğütücü Şişecam Ürünleri Sanayi ve Ticaret Limited Şirketi', tradeName: 'Çağlayan Şişecam',
}
/** The #661 `PurchaseOrderReadSummaryViewV1`: what GET list/detail carry. */
const summary = { ...commandSummary, supplierDisplay }
const line = {
  lineId: '55555555-5555-4555-8555-555555555555', lineNo: 1, kind: 'General', parentLineId: null,
  sourceRequestLineId: null, productId: '66666666-6666-4666-8666-666666666666', productVersion: '7',
  unitId: '77777777-7777-4777-8777-777777777777', ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10',
}

describe('purchasing read contract', () => {
  it('decodes exact list and detail shapes', () => {
    expect(decodePurchaseOrderList({ items: [summary], nextCursor: null })?.items[0].companyId).toBe(COMPANY_GDC)
    const detail = decodePurchaseOrderDetail({ order: summary, lines: [line] })
    expect(detail?.order.companyId).toBe(COMPANY_GDC)
    expect(detail?.lines[0].remaining).toBe('10')
    expect(detail?.acceptances).toEqual([])
    expect(decodePurchaseOrderCommandResponse({
      detail: { order: commandSummary, lines: [line] },
      isReplay: true,
      originalCorrelationId: 'legacy-corr',
    })?.detail.acceptances).toEqual([])
  })

  it('reads the record-bound supplier identity on list and detail exactly as the server sent it', () => {
    expect(decodePurchaseOrderList({ items: [summary], nextCursor: null })?.items[0].supplierDisplay).toEqual(supplierDisplay)
    expect(decodePurchaseOrderDetail({ order: summary, lines: [line] })?.order.supplierDisplay).toEqual(supplierDisplay)
    // A null trade name is a legitimate value, not a missing one.
    const noTrade = { ...summary, supplierDisplay: { ...supplierDisplay, tradeName: null } }
    expect(decodePurchaseOrderList({ items: [noTrade], nextCursor: null })?.items[0].supplierDisplay.tradeName).toBeNull()
    // The party id is compared as an id, not as a string: casing does not unbind it.
    const upper = { ...summary, supplierDisplay: { ...supplierDisplay, partyId: supplierDisplay.partyId.toUpperCase() } }
    expect(decodePurchaseOrderDetail({ order: upper, lines: [line] })).not.toBeNull()
  })

  it.each([
    ['missing', commandSummary],
    ['null', { ...commandSummary, supplierDisplay: null }],
    ['an extra field', { ...summary, supplierDisplay: { ...supplierDisplay, taxNumber: '1' } }],
    ['a missing field', { ...commandSummary, supplierDisplay: { partyId: supplierDisplay.partyId, code: 'CAR-000042', legalName: 'A' } }],
    ['an empty code', { ...summary, supplierDisplay: { ...supplierDisplay, code: '' } }],
    ['an empty legal name', { ...summary, supplierDisplay: { ...supplierDisplay, legalName: '' } }],
    ['a whitespace-only code', { ...summary, supplierDisplay: { ...supplierDisplay, code: '   ' } }],
    ['a whitespace-only legal name', { ...summary, supplierDisplay: { ...supplierDisplay, legalName: ' \t\n ' } }],
    ['an empty trade name', { ...summary, supplierDisplay: { ...supplierDisplay, tradeName: '' } }],
    ['a whitespace-only trade name', { ...summary, supplierDisplay: { ...supplierDisplay, tradeName: '   ' } }],
    ['a non-string trade name', { ...summary, supplierDisplay: { ...supplierDisplay, tradeName: 7 } }],
    ['a malformed party id', { ...summary, supplierDisplay: { ...supplierDisplay, partyId: 'not-a-guid' } }],
    ['a nil party id', { ...summary, supplierDisplay: { ...supplierDisplay, partyId: '00000000-0000-0000-0000-000000000000' } }],
    ['another party than the order names', { ...summary, supplierDisplay: { ...supplierDisplay, partyId: COMPANY_GDC } }],
  ])('fails closed on list and detail when supplierDisplay is %s', (_name, order) => {
    expect(decodePurchaseOrderList({ items: [order], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderDetail({ order, lines: [line] })).toBeNull()
  })

  it('keeps create/approve command responses on the legacy shape, and refuses a read-shaped one', () => {
    const command = decodePurchaseOrderCommandResponse({
      detail: { order: commandSummary, lines: [line] }, isReplay: false, originalCorrelationId: 'corr-1',
    })
    expect(command?.detail.order).toEqual(commandSummary)
    expect(command === null ? null : supplierDisplayOf(command.detail.order)).toBeNull()
    expect(decodePurchaseOrderCommandResponse({
      detail: { order: summary, lines: [line] }, isReplay: false, originalCorrelationId: 'corr-1',
    })).toBeNull()
  })

  it('offers an identity to the screen only when it is valid and bound to the order', () => {
    expect(supplierDisplayOf(decodePurchaseOrderDetail({ order: summary, lines: [line] })!.order)).toEqual(supplierDisplay)
    expect(supplierDisplayOf(commandSummary as never)).toBeNull()
    expect(supplierDisplayOf({ ...summary, supplierDisplay: { ...supplierDisplay, partyId: COMPANY_GDC } } as never)).toBeNull()
    expect(supplierDisplayOf({ ...summary, supplierDisplay: { ...supplierDisplay, code: '' } } as never)).toBeNull()
    expect(supplierDisplayOf({ ...summary, supplierDisplay: { ...supplierDisplay, tradeName: '  ' } } as never)).toBeNull()
  })

  it('checks the identity without rewriting it: surrounding spaces are kept exactly as sent', () => {
    const padded = { ...supplierDisplay, code: ' CAR-000042 ', legalName: ' Örnek A.Ş. ', tradeName: ' Örnek ' }
    expect(decodePurchaseOrderDetail({ order: { ...summary, supplierDisplay: padded }, lines: [line] })?.order.supplierDisplay).toEqual(padded)
  })

  it('decodes exact immutable acceptance history in details and command responses', () => {
    const acceptances = [{
      acceptanceId: '88888888-8888-4888-8888-888888888888', lineId: line.lineId,
      quantity: '4.25', recordedAtUtc: '2026-10-05T09:00:00Z',
    }]
    const detail = decodePurchaseOrderDetail({ order: summary, lines: [line], acceptances })
    expect(detail?.acceptances).toEqual(acceptances)
    expect(decodePurchaseOrderCommandResponse({
      detail: { order: commandSummary, lines: [line], acceptances },
      isReplay: false,
      originalCorrelationId: 'corr-1',
    })?.detail.acceptances).toEqual(acceptances)
  })

  it('fails closed on loose, malformed, duplicate or unlinked acceptance history', () => {
    const acceptance = {
      acceptanceId: '88888888-8888-4888-8888-888888888888', lineId: line.lineId,
      quantity: '4.25', recordedAtUtc: '2026-10-05T09:00:00Z',
    }
    expect(decodePurchaseOrderDetail({ order: summary, lines: [line], acceptances: [acceptance], extra: true })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [line], acceptances: [{ ...acceptance, extra: true }] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [line], acceptances: [{ ...acceptance, quantity: '0' }] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [line], acceptances: [{ ...acceptance, recordedAtUtc: 'not-an-instant' }] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [line], acceptances: [{ ...acceptance, lineId: COMPANY_GDC }] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [line], acceptances: [acceptance, acceptance] })).toBeNull()
  })

  it('fails closed on loose fields, bad quantities and a line-count mismatch', () => {
    expect(decodePurchaseOrderList({ items: [{ ...summary, extra: true }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderList({ items: [{ ...summary, companyId: 'not-a-guid' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderList({ items: [{ ...summary, companyId: '00000000-0000-0000-0000-000000000000' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [{ ...line, remaining: '-1' }] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: { ...summary, lineCount: 2 }, lines: [line] })).toBeNull()
  })

  it('fails closed on non-canonical versions, invalid instants, dates and line kinds', () => {
    expect(decodePurchaseOrderList({ items: [{ ...summary, version: '02' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderList({ items: [{ ...summary, updatedAtUtc: 'not-an-instant' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderList({ items: [{ ...summary, effectiveOn: '2026-02-30' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [{ ...line, kind: 'Unknown' }] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [{ ...line, productVersion: '9223372036854775808' }] })).toBeNull()
  })
})

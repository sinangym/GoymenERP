import { serviceFixture, serviceId as id } from './serviceOrderFixtures.testSupport'
import { decodeMixedServicePricePreview } from './mixedServicePriceRevisionApi'
import { decodePurchaseOrderCommandResponse } from './contract'
export { serviceId as mixedPriceId } from './serviceOrderFixtures.testSupport'
export function mixedPriceWire() {
  const base = serviceFixture(), at = '2026-11-28T10:00:00Z'
  const service = { ...base, version: '2', status: 'Approved' as const, isOpen: true }
  const order = { id: id(10), companyId: id(1), status: 'Approved' as const, version: '4', supplierPartyId: base.supplierPartyId,
    companyAccountId: base.companyAccountId, sourceRequestId: null, effectiveOn: base.businessDate, createdAtUtc: at, updatedAtUtc: at, lineCount: 1 }
  const line = { lineId: id(31), lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null, productId: id(32), productVersion: '7',
    unitId: id(8), ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10', cancelled: '0', price: null }
  return { companyId: id(1), parent: { order, lines: [line], acceptances: [], cancellation: null, priceDisclosure: 'Withheld' as const,
    currency: { currencyId: base.currencyId, currencyCode: base.currencyCode, scale: 2 }, pricePending: null },
    services: [{ service, initialPreparedVersion: '1', initialLineIds: service.lines.map(x => x.lineId), preparedBy: 'SENTETIK native maker',
      createdAtUtc: at, updatedAtUtc: at, priceRevision: 0, partyVersion: '1', accountVersion: '1',
      selection: { serviceOrderId: service.orderId, expectedVersion: service.version, lineIds: service.lines.map(x => x.lineId) } }] }
}
export function mixedPriceFixture() {
  const value = decodeMixedServicePricePreview(mixedPriceWire(), id(1), id(10), '4')
  if (value === null) throw new Error('Synthetic fixture must follow the actual native wire')
  return value
}
export function mixedPriceAnswer() {
  const detail = mixedPriceWire().parent
  const answer = decodePurchaseOrderCommandResponse({ detail: { ...detail, order: { ...detail.order, status: 'Draft', version: '5' }, lines: detail.lines.map(x => ({ ...x, committed: '0' })) },
    isReplay: false, originalCorrelationId: 'SENTETIK-price-original' })
  if (answer === null) throw new Error('Synthetic native answer must preserve the real command wire')
  return answer
}

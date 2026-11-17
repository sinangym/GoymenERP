export const serviceId = (n: number) => `7e000000-0000-0000-0000-${n.toString().padStart(12, '0')}`
export const serviceFixture = () => ({ companyId: serviceId(1), orderId: serviceId(2), documentNumber: 'SENTETIK-HIZ-2026-1', version: '1', kind: 'ServicePurchase' as const,
  status: 'Draft' as const, supplierPartyId: serviceId(3), companyAccountId: serviceId(4), businessDate: '2026-11-17', currencyId: serviceId(5), currencyCode: 'TRY',
  isOpen: false, priceDisclosure: 'Withheld' as const, lines: [{ lineId: serviceId(6), serviceTypeId: serviceId(7), serviceTypeVersion: '1', serviceName: 'SENTETIK hizmet',
    unitId: serviceId(8), unitCode: 'kg', quantity: '10', acceptedQuantity: '0', unitPrice: null, amount: null }], materials: [], expectedOutputs: [], acceptances: [] })

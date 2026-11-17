import type { AdditionalCost, AdditionalCostReferences, AdditionalCostSource } from './additionalCostContract'
export const expenseId = (n: number) => `11111111-1111-4111-8111-${n.toString().padStart(12, '0')}`
export function expenseSource(): AdditionalCostSource { return { companyId: expenseId(1), orderId: expenseId(2), orderVersion: '3',
  orderNumber: 'SENTETIK-SIP-2026-1', sourceKind: 'GoodsOrder', currencyId: expenseId(3), currencyCode: 'TRY', acceptances: [] } }
export function expenseReferences(): AdditionalCostReferences { return { companyId: expenseId(1), typeRequired: false, expenseTypes: [],
  businessDate: '2026-11-17', vatListVersion: '1', vatRates: [{ code: 'SENTETIK-20', name: 'SENTETIK KDV', percent: '20' }] } }
export function expenseFact(): AdditionalCost { return { companyId: expenseId(1), costId: expenseId(4), version: '1', orderId: expenseId(2),
  sourceOrderVersion: '3', sourceOrderNumber: 'SENTETIK-SIP-2026-1', sourceKind: 'GoodsOrder', acceptanceId: null,
  supplierPartyId: expenseId(5), companyAccountId: expenseId(6), businessDate: '2026-11-17', currencyId: expenseId(3), currencyCode: 'TRY',
  description: 'SENTETIK nakliye', expenseTypeId: null, expenseTypeVersion: null, expenseTypeName: null,
  netAmount: '100.01', vatPercent: '20', vatAmount: '20', grossAmount: '120.01', recordedBy: 'SENTETIK-actor', reason: 'SENTETIK belge',
  recordedAtUtc: '2026-11-17T00:00:00Z' } }

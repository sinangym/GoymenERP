import { COMPANY_A } from './testHarness'

/** SENTETIK transport fixture, not a business default. */
export function moneyProfileFixture() {
  return { companyId: COMPANY_A, today: '2026-11-16', profiles: [{
    currencyId: 'eeeeeeee-0000-4000-8000-00000000000e', currencyCode: 'TRY', operation: 'purchasing.order.v1', version: '1',
    versions: [{ profileId: 'dddddddd-0000-4000-8000-000000000001', version: '1', scale: 2, unitPriceScale: 4,
      isActive: true, effectiveFrom: '2026-01-01', effectiveThrough: null, state: 'InForce', sourceKind: 'CompiledBaselineV0',
      decisionReference: 'SENTETIK', recordedBy: null, recordedAtUtc: null, reason: null }],
  }] }
}

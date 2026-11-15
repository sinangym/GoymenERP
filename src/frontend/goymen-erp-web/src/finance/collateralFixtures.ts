/**
 * Test-only SENTETIK fixtures for the API-85b collateral screens (NOT_ENV). Every identity is synthetic.
 */

export const CO_COMPANY = '11111111-1111-4111-8111-111111111111'
export const CO_OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
export const CO_PARTY = 'dddddddd-0000-4000-8000-000000000004'
export const CO_ACCOUNT = 'eeeeeeee-0000-4000-8000-000000000005'
export const CO_OTHER_ACCOUNT = 'eeeeeeee-0000-4000-8000-000000000006'
export const CO_CURRENCY = '22222222-2222-4222-8222-222222222222'
export const CO_TYPE = '55555555-5555-4555-8555-555555555555'
export const CO_COLLATERAL = 'cccccccc-0000-4000-8000-000000000001'
export const coId = (n: number) => `aaaaaaaa-0000-4000-8000-${String(n).padStart(12, '0')}`

export const recorded = (patch: Record<string, unknown> = {}) => ({
  id: coId(1), companyId: CO_COMPANY, collateralId: CO_COLLATERAL, revision: '1', direction: 'Received',
  source: 'FinanceManualEntry', partyId: CO_PARTY, companyAccountId: CO_ACCOUNT, kind: 'Record', collateralTypeId: CO_TYPE,
  collateralTypeVersion: '1', currencyId: CO_CURRENCY, amount: '250000.50', validFrom: '2026-11-01', validUntil: null,
  documentReference: 'SENTETIK-BELGE-1', releaseReason: null, status: 'Effective',
  recordedAtUtc: '2026-11-14T08:00:00+00:00', supersededAtUtc: null, version: '1', ...patch,
})

export const changed = (patch: Record<string, unknown> = {}) => recorded({
  id: coId(2), revision: '2', kind: 'Change', amount: '300000', recordedAtUtc: '2026-11-14T09:00:00+00:00', ...patch,
})

export const released = (patch: Record<string, unknown> = {}) => recorded({
  id: coId(3), revision: '2', kind: 'Release', releaseReason: 'SENTETIK çözüm gerekçesi',
  recordedAtUtc: '2026-11-14T10:00:00+00:00', ...patch,
})

export const superseded = (patch: Record<string, unknown> = {}) =>
  recorded({ status: 'Superseded', supersededAtUtc: '2026-11-14T09:00:00+00:00', ...patch })

export const available = (amounts: readonly { currencyId: string; amount: string }[] = []) =>
  ({ state: 'Available', amounts, reasonCode: null })

export const unavailable = (reasonCode: string, state = 'Unavailable') => ({ state, amounts: [], reasonCode })

export const account = (patch: Record<string, unknown> = {}) => ({
  companyId: CO_COMPANY, companyAccountId: CO_ACCOUNT, held: [recorded()],
  weightedCollateral: available([{ currencyId: CO_CURRENCY, amount: '187500.375' }]),
  documentaryRisk: available([{ currencyId: CO_CURRENCY, amount: '40000' }]), ...patch,
})

export const historyPage = (items: readonly unknown[], companyAccountId = CO_ACCOUNT, companyId = CO_COMPANY) =>
  ({ companyId, companyAccountId, items })

export const typeCandidates = (items: readonly unknown[] = [typeCandidate()], companyId = CO_COMPANY) =>
  ({ companyId, items })

export const typeCandidate = (patch: Record<string, unknown> = {}) =>
  ({ collateralTypeId: CO_TYPE, code: 'SENTETIK-TUR', name: 'SENTETIK teminat türü', ratePercent: '75', version: '1',
    ...patch })

export const currencyCandidates = (companyId = CO_COMPANY) =>
  ({ companyId, items: [{ currencyId: CO_CURRENCY, code: 'TRY', displayLabel: 'Türk lirası' }] })

export const succeeded = (resource: unknown, patch: Record<string, unknown> = {}) =>
  ({ outcome: 'Succeeded', resource, isReplay: false, originalCorrelationId: 'corr-1', issues: [], ...patch })

export const refusal = (outcome: string, issues: readonly { field: string; code: string }[] = [],
  patch: Record<string, unknown> = {}) =>
  ({ outcome, resource: null, isReplay: false, originalCorrelationId: 'corr-1', issues, ...patch })

export const jsonResponse = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

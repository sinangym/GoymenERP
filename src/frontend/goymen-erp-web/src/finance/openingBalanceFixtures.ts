/**
 * Test-only SENTETIK fixtures for the FIN-S9 opening balance screen (NOT_ENV). Every identity is synthetic.
 */

export const OB_COMPANY = '11111111-1111-4111-8111-111111111111'
export const OB_OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
export const OB_CASH = 'eeeeeeee-0000-4000-8000-000000000001'
export const OB_ACCOUNT = 'eeeeeeee-0000-4000-8000-000000000002'
export const OB_PARTY = 'dddddddd-0000-4000-8000-000000000003'
export const OB_CURRENCY = '22222222-2222-4222-8222-222222222222'
export const obId = (n: number) => `aaaaaaaa-0000-4000-8000-${String(n).padStart(12, '0')}`

export const proposed = (patch: Record<string, unknown> = {}) => ({
  id: obId(1), companyId: OB_COMPANY, targetKind: 'CashAccount', targetId: OB_CASH, partyId: null,
  currencyId: OB_CURRENCY, direction: null, amount: '150000.50', asOfDate: '2026-11-01', status: 'Proposed',
  openingPeriodVersion: '7', isOwnProposal: false, proposedAtUtc: '2026-11-15T08:00:00+00:00', decidedAtUtc: null,
  rejectionReason: null, version: '1', reversal: null, ...patch,
})

export const partyProposed = (patch: Record<string, unknown> = {}) => proposed({
  id: obId(2), targetKind: 'PartyAccount', targetId: OB_ACCOUNT, partyId: OB_PARTY, direction: 'Debit',
  amount: '2500', ...patch,
})

export const approved = (patch: Record<string, unknown> = {}) =>
  proposed({ id: obId(3), status: 'Approved', decidedAtUtc: '2026-11-15T09:00:00+00:00', version: '2', ...patch })

export const rejected = (patch: Record<string, unknown> = {}) => proposed({
  id: obId(4), status: 'Rejected', decidedAtUtc: '2026-11-15T09:30:00+00:00',
  rejectionReason: 'SENTETIK red gerekçesi', version: '2', ...patch,
})

export const OB_REVERSAL = 'bbbbbbbb-0000-4000-8000-000000000005'

export const reversalProposed = (patch: Record<string, unknown> = {}) => ({
  id: OB_REVERSAL, status: 'Proposed', reason: 'SENTETIK ters kayıt gerekçesi', openingPeriodVersion: '7',
  isOwnProposal: false, proposedAtUtc: '2026-11-15T10:00:00+00:00', decidedAtUtc: null, rejectionReason: null,
  version: '1', ...patch,
})

export const approvedWithReversal = (reversal: Record<string, unknown> = {}, patch: Record<string, unknown> = {}) =>
  approved({ reversal: reversalProposed(reversal), ...patch })

export const reversed = (patch: Record<string, unknown> = {}) => approved({
  id: obId(5), status: 'Reversed', version: '3',
  reversal: reversalProposed({ status: 'Approved', decidedAtUtc: '2026-11-15T11:00:00+00:00', version: '2' }),
  ...patch,
})

export const listPage = (items: readonly unknown[], companyId = OB_COMPANY) => ({ companyId, items })

export const period = (state = 'Open', version: string | null = state === 'Unavailable' ? null : '7',
  companyId = OB_COMPANY) => ({ companyId, state, version })

export const currencyCandidates = (companyId = OB_COMPANY, items: readonly unknown[] =
  [{ currencyId: OB_CURRENCY, code: 'TRY', displayLabel: 'Türk lirası' }]) => ({ companyId, items })

export const succeeded = (resource: unknown, patch: Record<string, unknown> = {}) =>
  ({ outcome: 'Succeeded', resource, isReplay: false, originalCorrelationId: 'corr-1', issues: [], ...patch })

export const refusal = (outcome: string, issues: readonly { field: string; code: string }[] = [],
  patch: Record<string, unknown> = {}) =>
  ({ outcome, resource: null, isReplay: false, originalCorrelationId: 'corr-1', issues, ...patch })

export const jsonResponse = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

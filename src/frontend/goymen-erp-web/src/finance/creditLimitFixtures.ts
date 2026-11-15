/**
 * Test-only SENTETIK fixtures for the API-85a credit limit screens (NOT_ENV). Every identity is synthetic.
 * FNS-D45: every record is a proposal decided by a second person, so no fixture is a one-step (`isDirect`) record.
 */

export const CL_COMPANY = '11111111-1111-4111-8111-111111111111'
export const CL_OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
export const CL_PARTY = 'dddddddd-0000-4000-8000-000000000004'
export const CL_ACCOUNT = 'eeeeeeee-0000-4000-8000-000000000005'
export const CL_OTHER_ACCOUNT = 'eeeeeeee-0000-4000-8000-000000000006'
export const CL_CURRENCY = '22222222-2222-4222-8222-222222222222'
export const clId = (n: number) => `aaaaaaaa-0000-4000-8000-${String(n).padStart(12, '0')}`

export const approvedSet = (patch: Record<string, unknown> = {}) => ({
  id: clId(1), companyId: CL_COMPANY, partyId: CL_PARTY, companyAccountId: CL_ACCOUNT, kind: 'Set',
  currencyId: CL_CURRENCY, amount: '150000.50', status: 'Approved', isDirect: false, revision: '1', rejectionReason: null,
  proposedAtUtc: '2026-11-14T07:00:00+00:00', decidedAtUtc: '2026-11-14T08:00:00+00:00', supersededAtUtc: null,
  version: '2', ...patch,
})

export const proposedSet = (patch: Record<string, unknown> = {}) => ({
  id: clId(2), companyId: CL_COMPANY, partyId: CL_PARTY, companyAccountId: CL_ACCOUNT, kind: 'Set',
  currencyId: CL_CURRENCY, amount: '200000', status: 'Proposed', isDirect: false, revision: null, rejectionReason: null,
  proposedAtUtc: '2026-11-14T09:00:00+00:00', decidedAtUtc: null, supersededAtUtc: null, version: '1', ...patch,
})

export const rejected = (patch: Record<string, unknown> = {}) => proposedSet({
  id: clId(3), status: 'Rejected', rejectionReason: 'SENTETIK gerekçe', decidedAtUtc: '2026-11-14T10:00:00+00:00',
  version: '2', ...patch,
})

export const account = (patch: Record<string, unknown> = {}) => ({
  companyId: CL_COMPANY, partyId: CL_PARTY, companyAccountId: CL_ACCOUNT, effective: approvedSet(), pending: null,
  revision: '1', isOwnPending: false, ...patch,
})

export const listPage = (accounts: readonly unknown[], companyId = CL_COMPANY) =>
  ({ companyId, items: accounts.map((item) => ({ account: item })) })

export const historyPage = (items: readonly unknown[], companyAccountId = CL_ACCOUNT, companyId = CL_COMPANY) =>
  ({ companyId, companyAccountId, items })

export const candidates = (companyId = CL_COMPANY) =>
  ({ companyId, items: [{ currencyId: CL_CURRENCY, code: 'TRY', displayLabel: 'Türk lirası' }] })

export const succeeded = (resource: unknown, patch: Record<string, unknown> = {}) =>
  ({ outcome: 'Succeeded', resource, isReplay: false, originalCorrelationId: 'corr-1', issues: [], ...patch })

export const refusal = (outcome: string, issues: readonly { field: string; code: string }[] = [],
  patch: Record<string, unknown> = {}) =>
  ({ outcome, resource: null, isReplay: false, originalCorrelationId: 'corr-1', issues, ...patch })

export const jsonResponse = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

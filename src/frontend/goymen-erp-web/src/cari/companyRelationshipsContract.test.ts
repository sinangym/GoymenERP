import { describe, expect, it } from 'vitest'
import { decodeCompanyRelationships } from './companyRelationshipsContract'

const PARTY = '11111111-1111-1111-1111-111111111111'
const A = 'aaaaaaaa-0000-0000-0000-000000000001'
const B = 'bbbbbbbb-0000-0000-0000-000000000002'
const C = 'cccccccc-0000-0000-0000-000000000003'
const KNOWN = new Set([A, B])

function account(companyId: string, over: Record<string, unknown> = {}) {
  return {
    id: `${companyId.slice(0, 8)}-1111-1111-1111-111111111111`,
    companyId,
    roles: 1,
    isActive: true,
    version: '2',
    ...over,
  }
}

function body(accounts: unknown[], over: Record<string, unknown> = {}) {
  return {
    id: PARTY,
    code: 'CAR-000001',
    kind: 1,
    legalName: 'Örnek Metal',
    tradeName: null,
    isActive: true,
    version: '3',
    companyAccounts: accounts,
    ...over,
  }
}

describe('decodeCompanyRelationships', () => {
  it('keeps every authorized account in server order with its own state and roles', () => {
    const decoded = decodeCompanyRelationships(
      body([account(A), account(B, { roles: 0, isActive: false })]),
      PARTY,
      A,
      KNOWN,
    )
    expect(decoded).toEqual({
      partyId: PARTY,
      accounts: [
        { accountId: 'aaaaaaaa-1111-1111-1111-111111111111', companyId: A, roles: 1, isActive: true },
        { accountId: 'bbbbbbbb-1111-1111-1111-111111111111', companyId: B, roles: 0, isActive: false },
      ],
    })
  })

  it('carries no version and nothing the server adds later', () => {
    const decoded = decodeCompanyRelationships(
      body([account(A, { externalIntegrationCode: 'SECRET', balance: 10 })], { balance: 5 }),
      PARTY,
      A,
      KNOWN,
    )
    expect(JSON.stringify(decoded)).not.toMatch(/SECRET|balance|version/)
  })

  it('accepts a single account: the active company is the only one visible', () => {
    expect(decodeCompanyRelationships(body([account(A)]), PARTY, A, KNOWN)?.accounts).toHaveLength(1)
  })

  it.each([
    ['a company the shell directory does not list', body([account(A), account(C)])],
    ['the same company twice', body([account(A), account(A)])],
    ['no account for the active company', body([account(B)])],
    ['an empty account list', body([])],
    ['a different Party', body([account(A)], { id: '22222222-2222-2222-2222-222222222222' })],
    ['an unknown role bit', body([account(A, { roles: 16 })])],
    ['negative roles', body([account(A, { roles: -1 })])],
    ['a numeric version', body([account(A, { version: 2 })])],
    ['a version past Int64.MaxValue', body([account(A, { version: '9223372036854775808' })])],
    ['a nil account id', body([account(A, { id: '00000000-0000-0000-0000-000000000000' })])],
    ['a string isActive', body([account(A, { isActive: 'true' })])],
    ['a missing account list', body([account(A)], { companyAccounts: undefined })],
  ])('refuses the whole response for %s', (_label, raw) => {
    expect(decodeCompanyRelationships(raw, PARTY, A, KNOWN)).toBeNull()
  })

  it('refuses more accounts than the actor has companies', () => {
    expect(decodeCompanyRelationships(body([account(A), account(B)]), PARTY, A, new Set([A]))).toBeNull()
  })

  it('refuses when the requested ids are not bound UUIDs', () => {
    expect(decodeCompanyRelationships(body([account(A)]), 'not-a-uuid', A, KNOWN)).toBeNull()
    expect(decodeCompanyRelationships(body([account(A)]), PARTY, '', KNOWN)).toBeNull()
  })
})

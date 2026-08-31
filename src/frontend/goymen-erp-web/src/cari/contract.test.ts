import { describe, expect, it } from 'vitest'
import {
  decodeCounterpartyPage,
  problemCodeOf,
  rolesFromFlags,
  PARTY_KIND,
} from './contract'

const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
const PARTY_1 = '11111111-1111-1111-1111-111111111111'
const ACCOUNT_1 = 'cccccccc-1111-1111-1111-111111111111'
const ACCOUNT_2 = 'dddddddd-2222-2222-2222-222222222222'

function account(over: Record<string, unknown> = {}) {
  return { id: ACCOUNT_1, companyId: COMPANY_A, roles: 1, isActive: true, version: 1, ...over }
}
function item(over: Record<string, unknown> = {}) {
  return {
    id: PARTY_1,
    code: 'C-0001',
    kind: PARTY_KIND.LegalEntity,
    legalName: 'Test Ltd. Şti.',
    tradeName: null,
    isActive: true,
    version: 1,
    companyAccounts: [account()],
    ...over,
  }
}
function page(over: Record<string, unknown> = {}) {
  return {
    items: [item()],
    nextCursor: null,
    hasMore: false,
    totalCount: null,
    countScope: null,
    ...over,
  }
}

describe('rolesFromFlags — PartyRole is a bit flag, not a row per role', () => {
  it('decomposes combined flags in declaration order', () => {
    expect(rolesFromFlags(1)).toEqual(['Customer'])
    expect(rolesFromFlags(2)).toEqual(['Supplier'])
    // One account carrying BOTH customer and supplier (Codex #145 review)
    expect(rolesFromFlags(1 | 2)).toEqual(['Customer', 'Supplier'])
    expect(rolesFromFlags(15)).toEqual(['Customer', 'Supplier', 'Tolling', 'Service'])
  })
})

describe('decodeCounterpartyPage — happy path', () => {
  it('accepts a well-formed page', () => {
    const decoded = decodeCounterpartyPage(page())
    expect(decoded).not.toBeNull()
    expect(decoded!.items).toHaveLength(1)
    expect(decoded!.items[0].companyAccounts[0].roles).toBe(1)
  })

  it('keeps an out-of-JS-range totalCount verbatim as a string', () => {
    const huge = '9007199254740993' // Number.MAX_SAFE_INTEGER + 2
    const decoded = decodeCounterpartyPage(page({ totalCount: huge }))
    expect(decoded!.totalCount).toBe(huge)
  })

  it('normalises a numeric totalCount to a string without arithmetic', () => {
    expect(decodeCounterpartyPage(page({ totalCount: 42 }))!.totalCount).toBe('42')
  })

  it('accepts hasMore=true paired with a cursor', () => {
    const decoded = decodeCounterpartyPage(page({ hasMore: true, nextCursor: 'abc' }))
    expect(decoded!.hasMore).toBe(true)
    expect(decoded!.nextCursor).toBe('abc')
  })
})

describe('decodeCounterpartyPage — fails closed', () => {
  it('rejects an inconsistent hasMore / nextCursor pair', () => {
    expect(decodeCounterpartyPage(page({ hasMore: true, nextCursor: null }))).toBeNull()
    expect(decodeCounterpartyPage(page({ hasMore: false, nextCursor: 'abc' }))).toBeNull()
  })

  it('rejects an out-of-range PartyKind', () => {
    expect(decodeCounterpartyPage(page({ items: [item({ kind: 3 })] }))).toBeNull()
    expect(decodeCounterpartyPage(page({ items: [item({ kind: 0 })] }))).toBeNull()
  })

  it('rejects PartyRole flags outside 1..15 or with unknown bits', () => {
    for (const roles of [0, 16, 32, -1, 1.5]) {
      expect(
        decodeCounterpartyPage(page({ items: [item({ companyAccounts: [account({ roles })] })] })),
      ).toBeNull()
    }
  })

  it('rejects a nil or malformed UUID', () => {
    expect(
      decodeCounterpartyPage(page({ items: [item({ id: '00000000-0000-0000-0000-000000000000' })] })),
    ).toBeNull()
    expect(decodeCounterpartyPage(page({ items: [item({ id: 'not-a-uuid' })] }))).toBeNull()
  })

  it('rejects duplicate party ids in one page', () => {
    expect(decodeCounterpartyPage(page({ items: [item(), item()] }))).toBeNull()
  })

  it('rejects two accounts for the same company on one party', () => {
    const twoSameCompany = item({
      companyAccounts: [account(), account({ id: ACCOUNT_2, companyId: COMPANY_A })],
    })
    expect(decodeCounterpartyPage(page({ items: [twoSameCompany] }))).toBeNull()
  })

  it('accepts one account per distinct company', () => {
    const twoCompanies = item({
      companyAccounts: [account(), account({ id: ACCOUNT_2, companyId: COMPANY_B, roles: 2 })],
    })
    const decoded = decodeCounterpartyPage(page({ items: [twoCompanies] }))
    expect(decoded!.items[0].companyAccounts).toHaveLength(2)
  })

  it('rejects an empty code or legalName', () => {
    expect(decodeCounterpartyPage(page({ items: [item({ code: '   ' })] }))).toBeNull()
    expect(decodeCounterpartyPage(page({ items: [item({ legalName: '' })] }))).toBeNull()
  })

  it('rejects a negative or non-numeric totalCount', () => {
    expect(decodeCounterpartyPage(page({ totalCount: -1 }))).toBeNull()
    expect(decodeCounterpartyPage(page({ totalCount: 'abc' }))).toBeNull()
    expect(decodeCounterpartyPage(page({ totalCount: 1.5 }))).toBeNull()
  })

  it('rejects the whole page when a single row is bad (no partial page)', () => {
    expect(decodeCounterpartyPage(page({ items: [item(), item({ id: 'bad', code: 'X' })] }))).toBeNull()
  })

  it('rejects non-objects and a missing items array', () => {
    expect(decodeCounterpartyPage(null)).toBeNull()
    expect(decodeCounterpartyPage('nope')).toBeNull()
    expect(decodeCounterpartyPage({ hasMore: false })).toBeNull()
  })
})

describe('problemCodeOf — stable machine codes only', () => {
  it('reads a known code', () => {
    expect(problemCodeOf({ code: 'invalid_cursor', title: 'İmleç geçersiz' })).toBe('invalid_cursor')
  })

  it('ignores an unknown code and a localized title', () => {
    expect(problemCodeOf({ code: 'totally_made_up' })).toBeNull()
    expect(problemCodeOf({ title: 'İmleç geçersiz' })).toBeNull()
    expect(problemCodeOf(null)).toBeNull()
  })
})

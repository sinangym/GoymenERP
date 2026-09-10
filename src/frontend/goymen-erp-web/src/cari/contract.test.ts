import { describe, expect, it } from 'vitest'
import {
  decodeCounterpartyPage,
  problemCodeOf,
  rolesFromFlags,
  COUNT_SCOPE_FILTERED_AUTHORIZED,
  PARTY_KIND,
  type CounterpartyBinding,
} from './contract'

const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
const PARTY_1 = '11111111-1111-1111-1111-111111111111'
const ACCOUNT_1 = 'cccccccc-1111-1111-1111-111111111111'
const ACCOUNT_2 = 'dddddddd-2222-2222-2222-222222222222'

/** The C-01 slice always decodes bound to the active single company (JIT spec §3.5). */
const SINGLE_A: CounterpartyBinding = { mode: 'single', activeCompanyId: COMPANY_A }
const GROUP: CounterpartyBinding = { mode: 'group' }

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
    // One account carrying BOTH customer and supplier (Codex #145 review, JIT §3.x)
    expect(rolesFromFlags(1 | 2)).toEqual(['Customer', 'Supplier'])
    expect(rolesFromFlags(15)).toEqual(['Customer', 'Supplier', 'Tolling', 'Service'])
  })
})

describe('decodeCounterpartyPage — happy path', () => {
  it('accepts a well-formed page', () => {
    const decoded = decodeCounterpartyPage(page(), SINGLE_A)
    expect(decoded).not.toBeNull()
    expect(decoded!.items).toHaveLength(1)
    expect(decoded!.items[0].companyAccounts[0].roles).toBe(1)
  })

  it('keeps an out-of-JS-range totalCount verbatim as a string', () => {
    const huge = '9007199254740993' // Number.MAX_SAFE_INTEGER + 2
    const decoded = decodeCounterpartyPage(
      page({ totalCount: huge, countScope: COUNT_SCOPE_FILTERED_AUTHORIZED }),
      SINGLE_A,
      true,
    )
    expect(decoded!.totalCount).toBe(huge)
  })

  it('normalises a numeric totalCount to a string without arithmetic', () => {
    const decoded = decodeCounterpartyPage(
      page({ totalCount: 42, countScope: COUNT_SCOPE_FILTERED_AUTHORIZED }),
      SINGLE_A,
      true,
    )
    expect(decoded!.totalCount).toBe('42')
  })

  it('accepts hasMore=true paired with a cursor', () => {
    const decoded = decodeCounterpartyPage(page({ hasMore: true, nextCursor: 'abc' }), SINGLE_A)
    expect(decoded!.hasMore).toBe(true)
    expect(decoded!.nextCursor).toBe('abc')
  })
})

describe('decodeCounterpartyPage — company binding (JIT §3.5)', () => {
  it('single mode: a foreign company account rejects the WHOLE page', () => {
    const twoCompanies = item({
      companyAccounts: [account(), account({ id: ACCOUNT_2, companyId: COMPANY_B, roles: 2 })],
    })
    // Filtering the foreign row away would hide the fact that foreign data arrived.
    expect(decodeCounterpartyPage(page({ items: [twoCompanies] }), SINGLE_A)).toBeNull()
  })

  it('single mode: zero accounts rejects the page', () => {
    expect(
      decodeCounterpartyPage(page({ items: [item({ companyAccounts: [] })] }), SINGLE_A),
    ).toBeNull()
  })

  it('single mode: an account for another company only rejects the page', () => {
    const otherOnly = item({ companyAccounts: [account({ companyId: COMPANY_B })] })
    expect(decodeCounterpartyPage(page({ items: [otherOnly] }), SINGLE_A)).toBeNull()
  })

  it('group mode: one account per distinct company is allowed', () => {
    const twoCompanies = item({
      companyAccounts: [account(), account({ id: ACCOUNT_2, companyId: COMPANY_B, roles: 2 })],
    })
    const decoded = decodeCounterpartyPage(page({ items: [twoCompanies] }), GROUP)
    expect(decoded!.items[0].companyAccounts).toHaveLength(2)
  })

  it('rejects two accounts for the same company in either mode', () => {
    const dup = item({
      companyAccounts: [account(), account({ id: ACCOUNT_2, companyId: COMPANY_A })],
    })
    expect(decodeCounterpartyPage(page({ items: [dup] }), SINGLE_A)).toBeNull()
    expect(decodeCounterpartyPage(page({ items: [dup] }), GROUP)).toBeNull()
  })
})

describe('decodeCounterpartyPage — count scope (JIT §3.6)', () => {
  it('count and scope must travel together', () => {
    // a count without a scope could be read as "all parties"
    expect(
      decodeCounterpartyPage(page({ totalCount: 5, countScope: null }), SINGLE_A, true),
    ).toBeNull()
    // a scope without a count
    expect(
      decodeCounterpartyPage(
        page({ totalCount: null, countScope: COUNT_SCOPE_FILTERED_AUTHORIZED }),
        SINGLE_A,
        true,
      ),
    ).toBeNull()
  })

  it('rejects an unknown scope — we would not know what the number means', () => {
    expect(
      decodeCounterpartyPage(page({ totalCount: 5, countScope: 'all-parties' }), SINGLE_A, true),
    ).toBeNull()
  })

  it('accepts the one paired shape', () => {
    const decoded = decodeCounterpartyPage(
      page({ totalCount: 5, countScope: COUNT_SCOPE_FILTERED_AUTHORIZED }),
      SINGLE_A,
      true,
    )
    expect(decoded!.totalCount).toBe('5')
    expect(decoded!.countScope).toBe(COUNT_SCOPE_FILTERED_AUTHORIZED)
  })

  // Codex/ChatGPT #147 P1-1: both fields are REQUIRED by the contract. An absent
  // property is drift, not an implicit null.
  it('rejects a page missing either count property, and one missing both', () => {
    const { totalCount: _t, ...noTotal } = page()
    const { countScope: _s, ...noScope } = page()
    const { totalCount: _t2, countScope: _s2, ...neither } = page()
    expect(decodeCounterpartyPage(noTotal, SINGLE_A)).toBeNull()
    expect(decodeCounterpartyPage(noScope, SINGLE_A)).toBeNull()
    expect(decodeCounterpartyPage(neither, SINGLE_A)).toBeNull()
    // present-and-null is the valid no-count shape
    expect(decodeCounterpartyPage(page(), SINGLE_A)).not.toBeNull()
  })

  it('rejects a count we never asked for (includeCount=false)', () => {
    // The server volunteering a number means it counted something we did not specify,
    // so there is no label we could honestly put on it.
    const withCount = page({ totalCount: 5, countScope: COUNT_SCOPE_FILTERED_AUTHORIZED })
    expect(decodeCounterpartyPage(withCount, SINGLE_A, false)).toBeNull()
    expect(decodeCounterpartyPage(withCount, SINGLE_A, true)).not.toBeNull()
  })
})

describe('decodeCounterpartyPage — fails closed', () => {
  it('rejects an inconsistent hasMore / nextCursor pair', () => {
    expect(decodeCounterpartyPage(page({ hasMore: true, nextCursor: null }), SINGLE_A)).toBeNull()
    expect(decodeCounterpartyPage(page({ hasMore: false, nextCursor: 'abc' }), SINGLE_A)).toBeNull()
  })

  it('rejects an out-of-range PartyKind', () => {
    expect(decodeCounterpartyPage(page({ items: [item({ kind: 3 })] }), SINGLE_A)).toBeNull()
    expect(decodeCounterpartyPage(page({ items: [item({ kind: 0 })] }), SINGLE_A)).toBeNull()
  })

  it('resolves roles=0 — "no commercial classification" is an ordinary account', () => {
    // Not a relaxation of the range check: it is the range the 10 September decision
    // defines. Rejecting 0 made one role-less counterparty reject the WHOLE page.
    const decoded = decodeCounterpartyPage(
      page({ items: [item({ companyAccounts: [account({ roles: 0 })] })] }),
      SINGLE_A,
    )
    expect(decoded).not.toBeNull()
    expect(decoded!.items[0].companyAccounts[0].roles).toBe(0)
    expect(rolesFromFlags(decoded!.items[0].companyAccounts[0].roles)).toEqual([])
  })

  it('still rejects unknown bits, negatives and non-integers', () => {
    for (const roles of [16, 32, -1, 1.5]) {
      expect(
        decodeCounterpartyPage(
          page({ items: [item({ companyAccounts: [account({ roles })] })] }),
          SINGLE_A,
        ),
      ).toBeNull()
    }
  })

  it('rejects a nil or malformed UUID', () => {
    expect(
      decodeCounterpartyPage(
        page({ items: [item({ id: '00000000-0000-0000-0000-000000000000' })] }),
        SINGLE_A,
      ),
    ).toBeNull()
    expect(decodeCounterpartyPage(page({ items: [item({ id: 'not-a-uuid' })] }), SINGLE_A)).toBeNull()
  })

  it('rejects duplicate party ids in one page', () => {
    expect(decodeCounterpartyPage(page({ items: [item(), item()] }), SINGLE_A)).toBeNull()
  })

  it('rejects an empty code or legalName', () => {
    expect(decodeCounterpartyPage(page({ items: [item({ code: '   ' })] }), SINGLE_A)).toBeNull()
    expect(decodeCounterpartyPage(page({ items: [item({ legalName: '' })] }), SINGLE_A)).toBeNull()
  })

  it('rejects a negative or non-numeric totalCount', () => {
    for (const totalCount of [-1, 'abc', 1.5]) {
      expect(
        decodeCounterpartyPage(
          page({ totalCount, countScope: COUNT_SCOPE_FILTERED_AUTHORIZED }),
          SINGLE_A,
          true,
        ),
      ).toBeNull()
    }
  })

  it('rejects the whole page when a single row is bad (no partial page)', () => {
    expect(
      decodeCounterpartyPage(page({ items: [item(), item({ id: 'bad', code: 'X' })] }), SINGLE_A),
    ).toBeNull()
  })

  it('rejects non-objects and a missing items array', () => {
    expect(decodeCounterpartyPage(null, SINGLE_A)).toBeNull()
    expect(decodeCounterpartyPage('nope', SINGLE_A)).toBeNull()
    expect(decodeCounterpartyPage({ hasMore: false }, SINGLE_A)).toBeNull()
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

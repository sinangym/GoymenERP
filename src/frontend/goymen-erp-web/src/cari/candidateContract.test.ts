import { describe, expect, it } from 'vitest'
import { decodeLinkCandidatePage } from './candidateContract'
import { PARTY_KIND, PARTY_ROLE_FLAG } from './contract'

const TARGET_B = 'bbbbbbbb-0000-0000-0000-000000000002'
const SOURCE_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const SOURCE_C = 'cccccccc-0000-0000-0000-000000000003'
const PARTY_1 = '11111111-1111-1111-1111-111111111111'
const PARTY_2 = '22222222-2222-2222-2222-222222222222'
const ACCOUNT_1 = 'cccccccc-1111-1111-1111-111111111111'
const ACCOUNT_2 = 'dddddddd-2222-2222-2222-222222222222'

function source(over: Record<string, unknown> = {}) {
  return {
    id: ACCOUNT_1,
    companyId: SOURCE_A,
    roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier,
    isActive: true,
    version: '1',
    ...over,
  }
}

function item(over: Record<string, unknown> = {}) {
  return {
    id: PARTY_1,
    code: 'CAR-000001',
    kind: PARTY_KIND.LegalEntity,
    legalName: 'Yolculuk Test Metal A.Ş.',
    tradeName: 'Yolculuk Metal',
    isActive: true,
    visibleSourceAccounts: [source()],
    ...over,
  }
}

function page(items: unknown[], over: Record<string, unknown> = {}) {
  return { items, nextCursor: null, hasMore: false, ...over }
}

describe('decodeLinkCandidatePage — the candidate DTO is not the list DTO (handoff §3)', () => {
  it('accepts the journey candidate page scoped to the target company', () => {
    const decoded = decodeLinkCandidatePage(page([item()]), TARGET_B)
    expect(decoded).not.toBeNull()
    expect(decoded!.items).toHaveLength(1)
    expect(decoded!.items[0].visibleSourceAccounts[0].companyId).toBe(SOURCE_A)
    expect(decoded!.items[0].visibleSourceAccounts[0].version).toBe('1')
    expect(decoded!.hasMore).toBe(false)
  })

  it('accepts the empty page returned after the Party was linked', () => {
    const decoded = decodeLinkCandidatePage({ items: [], nextCursor: null, hasMore: false }, TARGET_B)
    expect(decoded).toEqual({ items: [], nextCursor: null, hasMore: false })
  })

  it('carries no Party version and does not require one', () => {
    const decoded = decodeLinkCandidatePage(page([item({ version: '4' })]), TARGET_B)
    expect(decoded).not.toBeNull()
    expect(Object.keys(decoded!.items[0])).toEqual([
      'id',
      'code',
      'kind',
      'legalName',
      'tradeName',
      'isActive',
      'visibleSourceAccounts',
    ])
  })

  it('rejects a numeric source-account version (string token only)', () => {
    expect(decodeLinkCandidatePage(page([item({ visibleSourceAccounts: [source({ version: 1 })] })]), TARGET_B)).toBeNull()
  })

  it('rejects `companyAccounts` on a candidate — a target-shaped account is a leak', () => {
    expect(decodeLinkCandidatePage(page([item({ companyAccounts: [] })]), TARGET_B)).toBeNull()
  })

  it('rejects a count — the endpoint has none and the browser invents none', () => {
    expect(decodeLinkCandidatePage(page([item()], { totalCount: 1 }), TARGET_B)).toBeNull()
    expect(decodeLinkCandidatePage(page([item()], { countScope: 'x' }), TARGET_B)).toBeNull()
  })

  it('rejects a source account in the TARGET company (would already be linked)', () => {
    expect(decodeLinkCandidatePage(page([item({ visibleSourceAccounts: [source({ companyId: TARGET_B })] })]), TARGET_B)).toBeNull()
  })

  it('rejects a candidate with no visible source (unseen Party)', () => {
    expect(decodeLinkCandidatePage(page([item({ visibleSourceAccounts: [] })]), TARGET_B)).toBeNull()
  })

  it('rejects a globally passive Party (not a candidate, §6.2) but keeps a passive SOURCE account', () => {
    expect(decodeLinkCandidatePage(page([item({ isActive: false })]), TARGET_B)).toBeNull()
    const passiveSource = decodeLinkCandidatePage(
      page([item({ visibleSourceAccounts: [source({ isActive: false })] })]),
      TARGET_B,
    )
    expect(passiveSource).not.toBeNull()
    expect(passiveSource!.items[0].visibleSourceAccounts[0].isActive).toBe(false)
  })

  it('accepts several visible sources from different companies, rejects duplicates', () => {
    const two = decodeLinkCandidatePage(
      page([item({ visibleSourceAccounts: [source(), source({ id: ACCOUNT_2, companyId: SOURCE_C })] })]),
      TARGET_B,
    )
    expect(two!.items[0].visibleSourceAccounts).toHaveLength(2)
    expect(
      decodeLinkCandidatePage(
        page([item({ visibleSourceAccounts: [source(), source({ id: ACCOUNT_2 })] })]),
        TARGET_B,
      ),
      'two accounts in the same source company',
    ).toBeNull()
    expect(
      decodeLinkCandidatePage(
        page([item({ visibleSourceAccounts: [source(), source({ companyId: SOURCE_C })] })]),
        TARGET_B,
      ),
      'duplicate account id',
    ).toBeNull()
  })

  it('rejects unknown role bits, duplicate Party ids and inconsistent paging', () => {
    expect(decodeLinkCandidatePage(page([item({ visibleSourceAccounts: [source({ roles: 16 })] })]), TARGET_B)).toBeNull()
    // …but 0 and 15 are inside the accepted range. A source account with no legacy
    // classification is an ordinary candidate, not a malformed page.
    for (const roles of [0, 15]) {
      expect(
        decodeLinkCandidatePage(page([item({ visibleSourceAccounts: [source({ roles })] })]), TARGET_B),
        `roles=${roles}`,
      ).not.toBeNull()
    }
    // Null and a missing property stay invalid: the response contract requires the number.
    for (const roles of [null, undefined]) {
      expect(
        decodeLinkCandidatePage(page([item({ visibleSourceAccounts: [source({ roles })] })]), TARGET_B),
        `roles=${String(roles)}`,
      ).toBeNull()
    }
    expect(decodeLinkCandidatePage(page([item(), item()]), TARGET_B)).toBeNull()
    expect(decodeLinkCandidatePage(page([item(), item({ id: PARTY_2 })]), TARGET_B)!.items).toHaveLength(2)
    expect(decodeLinkCandidatePage(page([item()], { hasMore: true }), TARGET_B)).toBeNull()
    expect(decodeLinkCandidatePage(page([item()], { nextCursor: 'c' }), TARGET_B)).toBeNull()
    expect(decodeLinkCandidatePage(page([item()], { hasMore: true, nextCursor: 'c' }), TARGET_B)!.nextCursor).toBe('c')
  })

  it('rejects a partial page rather than filtering the bad row out', () => {
    expect(decodeLinkCandidatePage(page([item(), item({ id: PARTY_2, kind: 9 })]), TARGET_B)).toBeNull()
  })

  it('rejects an invalid target binding', () => {
    expect(decodeLinkCandidatePage(page([item()]), 'not-a-uuid')).toBeNull()
  })
})

/** AT-002 — the global search decoder takes exactly the contract and nothing else (component evidence, NOT_ENV). */

import { describe, expect, it } from 'vitest'
import { PER_KIND_LIMIT, SEARCH_KIND, decodeSearchAnswer, normalizeTerm } from './searchContract'

const PARTY = '11111111-1111-1111-1111-111111111111'
const ORDER = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'

const item = (overrides: Record<string, unknown> = {}) => ({
  id: PARTY,
  code: 'CAR-000001',
  name: 'Zeta Çelik',
  secondaryName: null,
  state: 'Active',
  date: null,
  ...overrides,
})

const answer = (groups: unknown[], query = 'zeta') => ({ query, groups })

describe('AT-002 global search contract', () => {
  it('decodes grouped answers in the fixed kind order', () => {
    const decoded = decodeSearchAnswer(
      answer([
        { kind: SEARCH_KIND.party, items: [item()], hasMore: true },
        { kind: SEARCH_KIND.salesOrder, items: [item({ id: ORDER, code: 'SO-2026-000001', name: null, state: 'Approved', date: '2026-09-28' })], hasMore: false },
      ]),
      'zeta',
    )
    expect(decoded?.groups.map((group) => group.kind)).toEqual([SEARCH_KIND.party, SEARCH_KIND.salesOrder])
    expect(decoded?.groups[1].items[0].date).toBe('2026-09-28')
  })

  it('decodes an answer with no group (no readable kind looks the same as no match)', () => {
    expect(decodeSearchAnswer(answer([]), 'zeta')?.groups).toEqual([])
  })

  it.each([
    ['a price member on an item', answer([{ kind: SEARCH_KIND.salesOrder, items: [{ ...item(), unitPrice: '10' }], hasMore: false }])],
    ['a total on the answer', { ...answer([]), totalCount: 3 }],
    ['a count on a group', answer([{ kind: SEARCH_KIND.party, items: [], hasMore: false, count: 0 }])],
    ['an unknown kind', answer([{ kind: 'purchasing.order', items: [], hasMore: false }])],
    ['a repeated kind', answer([{ kind: SEARCH_KIND.party, items: [], hasMore: false }, { kind: SEARCH_KIND.party, items: [], hasMore: false }])],
    ['groups out of order', answer([{ kind: SEARCH_KIND.salesOrder, items: [], hasMore: false }, { kind: SEARCH_KIND.party, items: [], hasMore: false }])],
    ['more than the per-kind bound', answer([{ kind: SEARCH_KIND.party, items: Array.from({ length: PER_KIND_LIMIT + 1 }, (_, index) => item({ id: `11111111-1111-1111-1111-${String(index).padStart(12, '0')}` })), hasMore: true }])],
    ['a duplicated id', answer([{ kind: SEARCH_KIND.party, items: [item(), item()], hasMore: false }])],
    ['a malformed id', answer([{ kind: SEARCH_KIND.party, items: [item({ id: 'x' })], hasMore: false }])],
    ['an empty code', answer([{ kind: SEARCH_KIND.party, items: [item({ code: ' ' })], hasMore: false }])],
    ['a malformed date', answer([{ kind: SEARCH_KIND.party, items: [item({ date: '28.09.2026' })], hasMore: false }])],
    ['an answer for another term', answer([], 'other')],
  ])('refuses %s', (_, body) => {
    expect(decodeSearchAnswer(body, 'zeta')).toBeNull()
  })

  it('normalizes the term as the server does and refuses one outside the bounds', () => {
    expect(normalizeTerm('  Zeta ')).toBe('Zeta')
    expect(normalizeTerm('Çelik')).toBe('Çelik')
    expect(normalizeTerm(' a ')).toBeNull()
    expect(normalizeTerm('x'.repeat(101))).toBeNull()
    expect(normalizeTerm('x'.repeat(100))).toBe('x'.repeat(100))
    expect(normalizeTerm('ab\u0001')).toBeNull()
  })
})

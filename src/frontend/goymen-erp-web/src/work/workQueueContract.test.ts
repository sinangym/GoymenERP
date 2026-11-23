/** B-25 Work queue contract: the decoder takes the server's exact answer and refuses anything else whole. Synthetic. */

import { describe, expect, it } from 'vitest'
import {
  QUEUE_MAX_ITEMS_PER_SOURCE,
  QUEUE_MAX_LINK_LENGTH,
  belongsToCompany,
  decodeWorkQueue,
  isQueueKind,
  isQueueLink,
} from './workQueueContract'

const COMPANY = '10000000-0000-0000-0000-000000000001'
const ITEM = 'c6000000-0000-4000-8000-000000000001'
const SECOND = 'c6000000-0000-4000-8000-000000000002'
const FINANCE_KIND = 'finance.official-invoice-import.v1'
const PURCHASING_KIND = 'purchasing.request.v1'

const financeItem = {
  module: 'finance', itemId: ITEM, kind: FINANCE_KIND, createdAtUtc: '2026-11-01T07:00:00+00:00',
  link: `/onaylarim/${FINANCE_KIND}/${ITEM}`,
}
const purchasingItem = {
  module: 'purchasing', itemId: SECOND, kind: PURCHASING_KIND, createdAtUtc: '2026-11-02T07:00:00Z',
  link: `/satin-alma/talepler/${SECOND}?sekme=onay`,
}
const ready = (module: string, kind: string, hasMore = false) => ({ module, kind, state: 'Ready', hasMore })
const body = (overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY,
  items: [financeItem, purchasingItem],
  sources: [ready('finance', FINANCE_KIND), ready('purchasing', PURCHASING_KIND, true)],
  ...overrides,
})

describe('decodeWorkQueue', () => {
  it('decodes the exact answer, keeping the server order and each source state', () => {
    const queue = decodeWorkQueue(body())
    expect(queue).not.toBeNull()
    expect(queue!.items.map((item) => item.itemId)).toEqual([ITEM, SECOND])
    expect(queue!.sources.map((source) => [source.kind, source.state, source.hasMore]))
      .toEqual([[FINANCE_KIND, 'Ready', false], [PURCHASING_KIND, 'Ready', true]])
    expect(belongsToCompany(queue!, COMPANY.toUpperCase())).toBe(true)
    expect(belongsToCompany(queue!, '10000000-0000-0000-0000-000000000002')).toBe(false)
  })

  it('takes an empty queue and an unavailable source without items', () => {
    const queue = decodeWorkQueue(body({
      items: [],
      sources: [{ module: 'finance', kind: FINANCE_KIND, state: 'Unavailable', hasMore: false }],
    }))
    expect(queue?.items).toEqual([])
    expect(queue?.sources[0].state).toBe('Unavailable')
  })

  it.each([
    ['a missing member', () => ({ companyId: COMPANY, items: [] })],
    ['an extra member', () => ({ ...body(), nextCursor: null })],
    ['an empty company id', () => body({ companyId: '00000000-0000-0000-0000-000000000000' })],
    ['an item with an extra member', () => body({ items: [{ ...financeItem, approvalId: ITEM }] })],
    ['an item of an unlisted source', () => body({ sources: [ready('finance', FINANCE_KIND)] })],
    ['an item of an unavailable source', () => body({
      items: [financeItem],
      sources: [{ module: 'finance', kind: FINANCE_KIND, state: 'Unavailable', hasMore: false }],
    })],
    ['an unavailable source that claims more', () => body({
      items: [],
      sources: [{ module: 'finance', kind: FINANCE_KIND, state: 'Unavailable', hasMore: true }],
    })],
    ['an unknown source state', () => body({ items: [], sources: [{ ...ready('finance', FINANCE_KIND), state: 'Denied' }] })],
    ['a source listed twice', () => body({ items: [], sources: [ready('finance', FINANCE_KIND), ready('finance', FINANCE_KIND)] })],
    ['an item whose module is not its source\'s', () => body({ items: [{ ...financeItem, module: 'sales' }] })],
    ['a kind not owned by its module', () => body({
      items: [], sources: [ready('sales', FINANCE_KIND)],
    })],
    ['an item twice', () => body({ items: [financeItem, financeItem] })],
    ['an empty item id', () => body({ items: [{ ...financeItem, itemId: '00000000-0000-0000-0000-000000000000' }] })],
    ['a local instant', () => body({ items: [{ ...financeItem, createdAtUtc: '2026-11-01T10:00:00+03:00' }] })],
    ['an instant without offset', () => body({ items: [{ ...financeItem, createdAtUtc: '2026-11-01T07:00:00' }] })],
    ['an absolute link', () => body({ items: [{ ...financeItem, link: 'https://example.invalid/onaylarim' }] })],
    ['a protocol-relative link', () => body({ items: [{ ...financeItem, link: '//example.invalid/x' }] })],
    ['a non-boolean hasMore', () => body({ sources: [{ ...ready('finance', FINANCE_KIND), hasMore: 'false' }, ready('purchasing', PURCHASING_KIND)] })],
  ])('refuses %s', (_, make) => {
    expect(decodeWorkQueue(make())).toBeNull()
  })

  it('refuses more items of one source than the server bound', () => {
    const items = Array.from({ length: QUEUE_MAX_ITEMS_PER_SOURCE + 1 }, (_, index) => ({
      ...financeItem,
      itemId: `c6000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
    }))
    expect(decodeWorkQueue(body({ items: items.slice(0, -1), sources: [ready('finance', FINANCE_KIND)] }))).not.toBeNull()
    expect(decodeWorkQueue(body({ items, sources: [ready('finance', FINANCE_KIND)] }))).toBeNull()
  })

  it.each([null, [], 'queue', 1])('refuses a body that is not an object (%s)', (value) => {
    expect(decodeWorkQueue(value)).toBeNull()
  })
})

describe('isQueueKind', () => {
  it.each([
    ['finance', FINANCE_KIND, true],
    ['purchasing', 'purchasing.request.v12', true],
    ['purchasing', 'purchasing.request', false],
    ['purchasing', 'purchasing.request.v0', false],
    ['purchasing', 'Purchasing.request.v1', false],
    ['purchasing', 'purchasingx.request.v1', false],
    ['purchasing', 'sales.order.v1', false],
    ['Purchasing', 'Purchasing.request.v1', false],
  ])('%s / %s is %s', (module, kind, expected) => {
    expect(isQueueKind(module, kind)).toBe(expected)
  })
})

describe('isQueueLink', () => {
  it.each([
    `/onaylarim/${FINANCE_KIND}/${ITEM}`,
    '/satin-alma/talepler?onay=1&sekme=a+b',
    '/stok/etiket/%C5%9F',
    '/',
  ])('takes %s', (link) => {
    expect(isQueueLink(link)).toBe(true)
  })

  it.each([
    '',
    'onaylarim',
    '//host/x',
    '/a//b',
    '/a/../b',
    '/a/./b',
    '/a/%2e%2e/b',
    '/a/%2E/b',
    '/a/%2fb',
    '/a/%5Cb',
    '/a\\b',
    '/a#b',
    '/a b',
    'javascript:alert(1)',
    `/${'a'.repeat(QUEUE_MAX_LINK_LENGTH)}`,
  ])('refuses %s', (link) => {
    expect(isQueueLink(link)).toBe(false)
  })
})

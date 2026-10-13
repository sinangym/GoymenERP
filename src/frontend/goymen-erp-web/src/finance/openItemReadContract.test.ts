import { describe, expect, it } from 'vitest'
import { decodeOpenItem, decodeOpenItemFor, decodeOpenItemPage, OPEN_ITEM_CURSOR_MAX_LENGTH, OPEN_ITEM_PAGE_SIZE } from './openItemReadContract'

// Synthetic contract tests, NOT_ENV: the shapes are #700's `FinanceOpenItemViewV1` / `FinanceOpenItemPageV1`.
const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const CURRENCY = '22222222-2222-4222-8222-222222222222'
const id = (n: number) => `aaaaaaaa-0000-4000-8000-${String(n).padStart(12, '0')}`

const visible = (amount: string) => ({ disclosure: 'Visible', money: { amount, currencyId: CURRENCY } })
const redacted = { disclosure: 'Redacted', money: null }
const unavailable = { disclosure: 'Unavailable', money: null }

const wire = (patch: Record<string, unknown> = {}) => ({
  id: id(1), companyId: COMPANY, sourceKind: 'official.invoice-installment.v1', sourceId: id(2), sourceLineId: id(3),
  sourceVersion: '4', partyId: id(4), companyAccountId: id(5), currencyId: CURRENCY, version: '1', dueOn: '2026-10-31',
  status: 'Open', createdAtUtc: '2026-10-13T08:00:00+00:00', initialAmount: visible('1000.51'),
  outstandingAmount: visible('1000.51'), ...patch,
})
const metadataOnly = (patch: Record<string, unknown> = {}) =>
  wire({ status: null, initialAmount: redacted, outstandingAmount: redacted, ...patch })
const without = (key: string) => {
  const body: Record<string, unknown> = wire()
  delete body[key]
  return body
}

describe('FI04 open item decoder', () => {
  it('accepts the visible item exactly as sent', () => {
    expect(decodeOpenItem(wire(), COMPANY)).toEqual(wire())
    expect(decodeOpenItem(wire({ status: 'Closed', outstandingAmount: visible('0.00') }), COMPANY)?.outstandingAmount)
      .toEqual(visible('0.00'))
    expect(decodeOpenItem(wire({ outstandingAmount: visible('400.5') }), COMPANY)?.outstandingAmount.money?.amount).toBe('400.5')
    expect(decodeOpenItem(wire({ dueOn: null }), COMPANY)?.dueOn).toBeNull()
  })

  it('keeps a withheld amount and status withheld: no zero, no Closed, no money', () => {
    const item = decodeOpenItem(metadataOnly(), COMPANY)
    expect(item?.status).toBeNull()
    expect(item?.initialAmount).toEqual({ disclosure: 'Redacted', money: null })
    expect(item?.outstandingAmount).toEqual({ disclosure: 'Redacted', money: null })
    expect(decodeOpenItem(metadataOnly({ initialAmount: unavailable, outstandingAmount: unavailable }), COMPANY)?.initialAmount)
      .toEqual({ disclosure: 'Unavailable', money: null })
  })

  it('keeps an unknown source kind as the server sent it', () => {
    expect(decodeOpenItem(wire({ sourceKind: 'future.kind.v9' }), COMPANY)?.sourceKind).toBe('future.kind.v9')
  })

  it.each([
    ['another company', wire({ companyId: OTHER_COMPANY })],
    ['an extra field', wire({ total: '1' })],
    ['a missing status', without('status')],
    ['a missing due date', without('dueOn')],
    ['an unknown status', wire({ status: 'Overdue' })],
    ['a numeric status', wire({ status: 1 })],
    ['a status without amounts', wire({ initialAmount: redacted, outstandingAmount: redacted })],
    ['amounts without a status', wire({ status: null })],
    ['mixed disclosures', wire({ status: null, initialAmount: redacted, outstandingAmount: visible('1.00') })],
    ['money under a redacted disclosure', metadataOnly({ initialAmount: { disclosure: 'Redacted', money: { amount: '1.00', currencyId: CURRENCY } } })],
    ['a visible amount without money', wire({ initialAmount: { disclosure: 'Visible', money: null } })],
    ['an unknown disclosure', metadataOnly({ initialAmount: { disclosure: 'Hidden', money: null }, outstandingAmount: { disclosure: 'Hidden', money: null } })],
    ['a numeric amount', wire({ initialAmount: { disclosure: 'Visible', money: { amount: 1000.51, currencyId: CURRENCY } } })],
    ['a zero initial amount', wire({ initialAmount: visible('0.00'), outstandingAmount: visible('0.00') })],
    ['a negative remaining amount', wire({ outstandingAmount: visible('-1.00') })],
    ['more remaining than initial', wire({ outstandingAmount: visible('1000.52') })],
    ['money in another currency', wire({ initialAmount: { disclosure: 'Visible', money: { amount: '1000.51', currencyId: id(9) } } })],
    ['an extra money field', wire({ initialAmount: { disclosure: 'Visible', money: { amount: '1000.51', currencyId: CURRENCY, code: 'TRY' } } })],
    ['a nil identity', wire({ partyId: '00000000-0000-0000-0000-000000000000' })],
    ['a non-uuid source', wire({ sourceId: 'INV-1' })],
    ['a blank source kind', wire({ sourceKind: ' ' })],
    ['a padded source kind', wire({ sourceKind: ' official.invoice-installment.v1' })],
    ['a source kind longer than the column', wire({ sourceKind: 'k'.repeat(129) })],
    ['a blank source version', wire({ sourceVersion: '' })],
    ['a zero version', wire({ version: '0' })],
    ['a numeric version', wire({ version: 1 })],
    ['an impossible due date', wire({ dueOn: '2026-02-30' })],
    ['a due instant', wire({ dueOn: '2026-10-31T00:00:00Z' })],
    ['a creation date without time', wire({ createdAtUtc: '2026-10-13' })],
    ['an array', [wire()]],
    ['null', null],
  ])('refuses %s', (_name, body) => {
    expect(decodeOpenItem(body, COMPANY)).toBeNull()
  })

  it('accepts the detail only for the item that was asked for', () => {
    expect(decodeOpenItemFor(wire(), COMPANY, id(1).toUpperCase())?.id).toBe(id(1))
    expect(decodeOpenItemFor(wire(), COMPANY, id(7))).toBeNull()
    expect(decodeOpenItemFor({ item: wire() }, COMPANY, id(1))).toBeNull()
  })
})

describe('FI04 open item page decoder', () => {
  const page = (patch: Record<string, unknown> = {}) => ({ companyId: COMPANY, items: [wire(), wire({ id: id(6) })], nextCursor: 'opaque+/=', ...patch })

  it('accepts a page, its opaque cursor verbatim, the last page and an empty page', () => {
    const decoded = decodeOpenItemPage(page(), COMPANY)
    expect(decoded?.items.map((item) => item.id)).toEqual([id(1), id(6)])
    expect(decoded?.nextCursor).toBe('opaque+/=')
    expect(decodeOpenItemPage(page({ nextCursor: null }), COMPANY)?.nextCursor).toBeNull()
    expect(decodeOpenItemPage(page({ items: [], nextCursor: null }), COMPANY)?.items).toEqual([])
    const full = Array.from({ length: OPEN_ITEM_PAGE_SIZE }, (_unused, index) => wire({ id: id(100 + index) }))
    expect(decodeOpenItemPage(page({ items: full }), COMPANY)?.items).toHaveLength(OPEN_ITEM_PAGE_SIZE)
  })

  it.each([
    ['another company', page({ companyId: OTHER_COMPANY })],
    ['one item of another company', page({ items: [wire(), wire({ id: id(6), companyId: OTHER_COMPANY })] })],
    ['one malformed item', page({ items: [wire(), { id: id(6) }] })],
    ['the same item twice', page({ items: [wire(), wire()] })],
    ['disclosed and withheld rows in one answer', page({ items: [wire(), metadataOnly({ id: id(6) })] })],
    ['withheld and unavailable rows in one answer', page({ items: [metadataOnly(), metadataOnly({ id: id(6), initialAmount: unavailable, outstandingAmount: unavailable })] })],
    ['more items than the server page', page({ items: Array.from({ length: OPEN_ITEM_PAGE_SIZE + 1 }, (_unused, index) => wire({ id: id(100 + index) })) })],
    ['a blank cursor', page({ nextCursor: ' ' })],
    ['a cursor longer than the server accepts', page({ nextCursor: 'c'.repeat(OPEN_ITEM_CURSOR_MAX_LENGTH + 1) })],
    ['a missing cursor', { companyId: COMPANY, items: [] }],
    ['a total', page({ totalCount: 2 })],
    ['a bare array', [wire()]],
    ['items that are no array', page({ items: {} })],
  ])('refuses %s', (_name, body) => {
    expect(decodeOpenItemPage(body, COMPANY)).toBeNull()
  })
})

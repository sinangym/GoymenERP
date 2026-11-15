/** URT-S13 monthly scrap price decoder — fail-closed shape checks over SENTETIK values. */

import { describe, expect, it } from 'vitest'
import {
  canonicalScrapPrice,
  decodeScrapPrice,
  decodeScrapPriceList,
  isScrapPriceEntryText,
  monthsWithoutPrice,
} from './scrapPriceContract'

const YEAR = 2026

function view(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    scrapPriceId: 'f0000000-0000-4000-8000-000000000001',
    year: YEAR,
    month: 3,
    tlPerKg: '7.25',
    recordedBy: 'sentetik-giren',
    recordedAtUtc: '2026-03-01T08:00:00+00:00',
    ...overrides,
  }
}

describe('decodeScrapPrice', () => {
  it('accepts the contract exactly', () => {
    expect(decodeScrapPrice(view())).toEqual(view())
  })

  it.each([
    ['an extra key', { ...view(), material: 'x' }],
    ['a missing key', (({ recordedBy: _omit, ...rest }) => rest)(view())],
    ['a numeric price', view({ tlPerKg: 7.25 })],
    ['a zero price', view({ tlPerKg: '0' })],
    ['a trailing fractional zero', view({ tlPerKg: '7.250' })],
    ['a comma price', view({ tlPerKg: '7,25' })],
    ['month 0', view({ month: 0 })],
    ['month 13', view({ month: 13 })],
    ['a nil id', view({ scrapPriceId: '00000000-0000-0000-0000-000000000000' })],
    ['an instant without offset', view({ recordedAtUtc: '2026-03-01T08:00:00' })],
    ['a padded actor', view({ recordedBy: ' sentetik ' })],
    ['an actor over the bound', view({ recordedBy: 'a'.repeat(129) })],
    ['null', null],
  ])('rejects %s', (_name, value) => {
    expect(decodeScrapPrice(value)).toBeNull()
  })
})

describe('decodeScrapPriceList', () => {
  it('accepts the requested year latest first', () => {
    const list = decodeScrapPriceList({ year: YEAR, items: [view({ month: 10 }), view({ month: 3 })] }, YEAR)
    expect(list?.items.map((item) => item.month)).toEqual([10, 3])
  })

  it.each([
    ['another year', { year: YEAR - 1, items: [] }],
    ['a row of another year', { year: YEAR, items: [view({ year: YEAR - 1 })] }],
    ['ascending months', { year: YEAR, items: [view({ month: 3 }), view({ month: 10 })] }],
    ['a month twice', { year: YEAR, items: [view({ month: 3 }), view({ month: 3 })] }],
    ['one bad row', { year: YEAR, items: [view({ month: 10 }), view({ tlPerKg: 1 })] }],
    ['an extra key', { year: YEAR, items: [], nextCursor: null }],
    ['items not an array', { year: YEAR, items: {} }],
  ])('rejects %s', (_name, value) => {
    expect(decodeScrapPriceList(value, YEAR)).toBeNull()
  })
})

describe('entry text', () => {
  it.each(['7', '7.25', '0.5', '7.12345'])('accepts %s as a shape', (value) => {
    expect(isScrapPriceEntryText(value)).toBe(true)
  })

  it.each(['', '7,25', '-1', '+1', '07', '1e3', ' 7', '7.', '.5', '1 000'])('rejects %s', (value) => {
    expect(isScrapPriceEntryText(value)).toBe(false)
  })

  it.each([
    ['7.250', '7.25'],
    ['7.000', '7'],
    ['70', '70'],
    ['0.5', '0.5'],
  ])('canonicalises %s to %s', (value, expected) => {
    expect(canonicalScrapPrice(value)).toBe(expected)
  })
})

describe('monthsWithoutPrice', () => {
  it('names every calendar month the list does not carry', () => {
    const list = decodeScrapPriceList({ year: YEAR, items: [view({ month: 12 }), view({ month: 1 })] }, YEAR)
    expect(list).not.toBeNull()
    expect(monthsWithoutPrice(list!)).toEqual([2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
  })
})

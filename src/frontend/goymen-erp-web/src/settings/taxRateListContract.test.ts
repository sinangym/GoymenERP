/**
 * F-029 S9 (SAT-D181) rate list decoder and editor parser cases. Synthetic (SENTETIK) fixtures; the decoder fails CLOSED:
 * a missing, extra, mistyped or null member, another company's answer, an off-timeline version or a percent out of its
 * kind's range invalidates the whole answer.
 */

import { describe, expect, it } from 'vitest'
import {
  decodeRateLists,
  isRateListPercent,
  parseRateListText,
  rateListText,
  rateListWithdrawalPath,
} from './taxRateListContract'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER = 'bbbbbbbb-0000-4000-8000-000000000002'
const USER = '99999999-9999-4999-8999-999999999999'

type Json = Record<string, unknown>

function version(listVersion: string, effectiveFrom: string, entries: Json[]): Json {
  return {
    listVersion,
    effectiveFrom,
    entries,
    recordedBy: USER,
    recordedAtUtc: '2026-11-15T09:00:00+00:00',
    reason: 'SENTETIK',
  }
}

function vatEntry(overrides: Json = {}): Json {
  return { code: '20', name: 'SENTETIK genel', percent: '20', taxCode: null, ...overrides }
}

function withholdingEntry(overrides: Json = {}): Json {
  return { code: '7/10', name: 'SENTETIK demir', percent: '70', taxCode: 'T1', ...overrides }
}

function view(overrides: { vat?: Json; withholding?: Json; root?: Json } = {}): Json {
  return {
    companyId: COMPANY,
    businessDate: '2026-11-15',
    lists: [
      {
        kind: 'VatRate',
        version: '2',
        inForce: version('1', '2026-11-15', [vatEntry({ code: '0', percent: '0' }), vatEntry()]),
        scheduled: [version('2', '2026-12-01', [vatEntry()])],
        ...overrides.vat,
      },
      {
        kind: 'WithholdingRatio',
        version: '1',
        inForce: version('1', '2026-11-01', [withholdingEntry(), withholdingEntry({ code: '5/10', percent: '50', taxCode: null })]),
        scheduled: [],
        ...overrides.withholding,
      },
    ],
    ...overrides.root,
  }
}

function withoutKey(value: Json, key: string): Json {
  const copy = { ...value }
  delete copy[key]
  return copy
}

describe('decodeRateLists', () => {
  it('accepts both lists with their versions, percents as text and an optional GİB code', () => {
    const decoded = decodeRateLists(view(), COMPANY)
    expect(decoded?.lists.map((list) => list.kind)).toEqual(['VatRate', 'WithholdingRatio'])
    expect(decoded?.lists[0].inForce?.entries.map((entry) => entry.percent)).toEqual(['0', '20'])
    expect(decoded?.lists[1].inForce?.entries.map((entry) => entry.taxCode)).toEqual(['T1', null])
    expect(decoded?.lists[0].scheduled[0].listVersion).toBe('2')
  })

  it('accepts two lists that were never entered (SAT-D148: empty, version "0")', () => {
    const empty = { version: '0', inForce: null, scheduled: [] }
    const decoded = decodeRateLists(view({ vat: empty, withholding: empty }), COMPANY)
    expect(decoded?.lists.every((list) => list.inForce === null && list.version === '0')).toBe(true)
  })

  it('refuses another company, a missing, extra, mistyped or null root member', () => {
    expect(decodeRateLists(view(), OTHER)).toBeNull()
    expect(decodeRateLists(withoutKey(view(), 'businessDate'), COMPANY)).toBeNull()
    expect(decodeRateLists({ ...view(), extra: true }, COMPANY)).toBeNull()
    expect(decodeRateLists(view({ root: { lists: {} } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ root: { companyId: null } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ root: { businessDate: '2026-02-30' } }), COMPANY)).toBeNull()
    expect(decodeRateLists(null, COMPANY)).toBeNull()
    expect(decodeRateLists([], COMPANY)).toBeNull()
  })

  it('refuses a missing, repeated or unknown kind', () => {
    const lists = view().lists as Json[]
    expect(decodeRateLists(view({ root: { lists: [lists[0]] } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ root: { lists: [lists[0], lists[0]] } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { kind: 'Exemption' } }), COMPANY)).toBeNull()
  })

  it('refuses a malformed list or version member', () => {
    expect(decodeRateLists(view({ vat: { version: '01' } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { version: 2 } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { version: '9223372036854775808' } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { scheduled: null } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { extra: 1 } }), COMPANY)).toBeNull()
    const inForce = version('1', '2026-11-15', [vatEntry()])
    expect(decodeRateLists(view({ vat: { inForce: { ...inForce, listVersion: '0' } } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { inForce: { ...inForce, entries: [] } } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { inForce: { ...inForce, recordedBy: ' ' } } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { inForce: { ...inForce, recordedAtUtc: '2026-11-15T09:00:00' } } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { inForce: { ...inForce, reason: null } } }), COMPANY)).toBeNull()
    expect(decodeRateLists(view({ vat: { inForce: withoutKey(inForce, 'reason') } }), COMPANY)).toBeNull()
  })

  it('refuses an off-timeline answer', () => {
    // The version in force has not started yet.
    expect(decodeRateLists(view({ vat: { inForce: version('1', '2026-11-16', [vatEntry()]) } }), COMPANY)).toBeNull()
    // A scheduled version that already started, or two on one date / out of order.
    expect(decodeRateLists(view({ vat: { scheduled: [version('2', '2026-11-15', [vatEntry()])] } }), COMPANY)).toBeNull()
    expect(
      decodeRateLists(
        view({ vat: { scheduled: [version('2', '2026-12-02', [vatEntry()]), version('3', '2026-12-01', [vatEntry()])] } }),
        COMPANY,
      ),
    ).toBeNull()
    // A list never entered carries no version.
    expect(decodeRateLists(view({ vat: { version: '0' } }), COMPANY)).toBeNull()
  })

  it('refuses an entry off its kind: a percent out of range, a VAT GİB code, a repeated or malformed code', () => {
    const vatWith = (entry: Json) => view({ vat: { inForce: version('1', '2026-11-15', [entry]), scheduled: [] } })
    const withholdingWith = (entry: Json) => view({ withholding: { inForce: version('1', '2026-11-01', [entry]) } })
    expect(decodeRateLists(vatWith(vatEntry({ percent: '100.0001' })), COMPANY)).toBeNull()
    expect(decodeRateLists(vatWith(vatEntry({ percent: '20.00001' })), COMPANY)).toBeNull()
    expect(decodeRateLists(vatWith(vatEntry({ percent: 20 })), COMPANY)).toBeNull()
    expect(decodeRateLists(vatWith(vatEntry({ taxCode: 'T1' })), COMPANY)).toBeNull()
    expect(decodeRateLists(vatWith(vatEntry({ code: 'kdv' })), COMPANY)).toBeNull()
    expect(decodeRateLists(vatWith(vatEntry({ name: '' })), COMPANY)).toBeNull()
    expect(decodeRateLists(vatWith(withoutKey(vatEntry(), 'taxCode')), COMPANY)).toBeNull()
    expect(decodeRateLists(withholdingWith(withholdingEntry({ percent: '0' })), COMPANY)).toBeNull()
    expect(decodeRateLists(withholdingWith(withholdingEntry({ taxCode: 't1' })), COMPANY)).toBeNull()
    expect(decodeRateLists(withholdingWith(withholdingEntry({ taxCode: '' })), COMPANY)).toBeNull()
    const repeated = view({ vat: { inForce: version('1', '2026-11-15', [vatEntry(), vatEntry()]), scheduled: [] } })
    expect(decodeRateLists(repeated, COMPANY)).toBeNull()
  })
})

describe('isRateListPercent', () => {
  it('keeps the kind ranges as text checks', () => {
    expect(['0', '0.5', '20', '100', '100.0000'].every((value) => isRateListPercent('VatRate', value))).toBe(true)
    expect(['0.0001', '50', '100'].every((value) => isRateListPercent('WithholdingRatio', value))).toBe(true)
    expect(isRateListPercent('WithholdingRatio', '0.0000')).toBe(false)
    expect(['-1', '101', '1e2', '01', '.5', '5.', ' 5'].some((value) => isRateListPercent('VatRate', value))).toBe(false)
  })
})

describe('parseRateListText / rateListText', () => {
  it('parses VAT lines and refuses a GİB code, a malformed line or a repeated code', () => {
    expect(parseRateListText('VatRate', ' 20 ; SENTETIK genel ; 20 \n\n1;SENTETIK;1')).toEqual([
      { code: '20', name: 'SENTETIK genel', percent: '20', taxCode: null },
      { code: '1', name: 'SENTETIK', percent: '1', taxCode: null },
    ])
    expect(parseRateListText('VatRate', '20;SENTETIK;20;T1')).toBeNull()
    expect(parseRateListText('VatRate', '20;SENTETIK')).toBeNull()
    expect(parseRateListText('VatRate', '20;SENTETIK;20\n20;SENTETIK;10')).toBeNull()
    expect(parseRateListText('VatRate', '20;;20')).toBeNull()
    expect(parseRateListText('VatRate', '')).toBeNull()
  })

  it('parses withholding lines with an optional GİB code, upper-cased, and refuses a zero ratio', () => {
    expect(parseRateListText('WithholdingRatio', '7/10;SENTETIK demir;70; t1 \n5/10;SENTETIK hurda;50')).toEqual([
      { code: '7/10', name: 'SENTETIK demir', percent: '70', taxCode: 'T1' },
      { code: '5/10', name: 'SENTETIK hurda', percent: '50', taxCode: null },
    ])
    expect(parseRateListText('WithholdingRatio', '0;SENTETIK;0')).toBeNull()
    expect(parseRateListText('WithholdingRatio', '7/10;SENTETIK;70;T-1')).toBeNull()
  })

  it('refuses more entries than the transport bound', () => {
    const lines = Array.from({ length: 101 }, (_, index) => `${index};SENTETIK;1`).join('\n')
    expect(parseRateListText('VatRate', lines)).toBeNull()
  })

  it('writes the editor text back losslessly', () => {
    const text = '7/10;SENTETIK demir;70;T1\n5/10;SENTETIK hurda;50'
    expect(rateListText(parseRateListText('WithholdingRatio', text) ?? [])).toBe(text)
  })

  it('escapes the list version in the withdrawal path', () => {
    expect(rateListWithdrawalPath('VatRate', '2')).toBe('/settings/tax-master/rate-lists/VatRate/versions/2/withdrawal')
  })
})

/**
 * F-025 / B-11 decoders and helpers: every answer fails CLOSED (an extra, missing or off-list member, another company or
 * range, a rate on a day that has none) and an Unavailable resolution can never carry a rate ("kur yoksa işlem durur").
 * Fixtures are SENTETIK.
 */

import { describe, expect, it } from 'vitest'
import {
  addDays,
  decodeExchangeRateDayList,
  decodeImportSummary,
  decodeResolution,
  inclusiveDayCount,
  isForeignCurrency,
  isRateText,
  normalizeCurrency,
  todayInIstanbul,
} from './exchangeRateContract'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER_COMPANY = 'bbbbbbbb-0000-4000-8000-000000000002'
const RECORD = '71000000-0000-4000-8000-0000000000b1'
const SOURCE = '71000000-0000-4000-8000-0000000000c1'
const TARGET = '71000000-0000-4000-8000-0000000000c2'
const FROM = '2026-11-10'
const TO = '2026-11-13'

function record(overrides: Record<string, unknown> = {}) {
  return {
    recordId: RECORD,
    recordVersion: '1',
    kind: 'Bulletin',
    provenance: 'Official',
    state: 'Published',
    rates: [{ currencyCode: 'USD', sellingRate: '34.1234' }],
    recordedBy: 'SENTETIK içe aktarım',
    recordedAtUtc: '2026-11-13T13:00:00+00:00',
    reason: null,
    sourceReference: 'SENTETIK bülten',
    ...overrides,
  }
}

function day(overrides: Record<string, unknown> = {}) {
  return {
    date: TO,
    version: '2',
    bulletinState: 'Published',
    halfDayState: 'FullDay',
    halfDayProvenance: 'Official',
    rates: [{ currencyCode: 'USD', sellingRate: '34.1234', provenance: 'Official', recordId: RECORD, recordVersion: '1' }],
    records: [record()],
    ...overrides,
  }
}

const EMPTY_DAY = {
  date: FROM,
  version: '0',
  bulletinState: 'NotRecorded',
  halfDayState: 'Undetermined',
  halfDayProvenance: null,
  rates: [],
  records: [],
}

function list(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY,
    from: FROM,
    to: TO,
    bulletinSourceEnabled: true,
    halfDaySourceEnabled: false,
    days: [EMPTY_DAY, day()],
    ...overrides,
  }
}

const decodeList = (raw: unknown) => decodeExchangeRateDayList(raw, COMPANY, FROM, TO)

describe('decodeExchangeRateDayList', () => {
  it('decodes a valid list, keeping the rates as text', () => {
    const decoded = decodeList(list())
    expect(decoded?.days).toHaveLength(2)
    expect(decoded?.days[1].rates[0].sellingRate).toBe('34.1234')
    expect(decoded?.days[0].version).toBe('0')
    expect(decoded?.halfDaySourceEnabled).toBe(false)
  })

  it('refuses an extra or a missing member at every level', () => {
    expect(decodeList({ ...list(), extra: 1 })).toBeNull()
    const missing: Record<string, unknown> = list()
    delete missing.halfDaySourceEnabled
    expect(decodeList(missing)).toBeNull()
    expect(decodeList(list({ days: [{ ...day(), extra: 1 }] }))).toBeNull()
    expect(decodeList(list({ days: [day({ records: [{ ...record(), extra: 1 }] })] }))).toBeNull()
    expect(decodeList(list({ days: [day({ rates: [{ currencyCode: 'USD', sellingRate: '34.1' }] })] }))).toBeNull()
  })

  it('refuses another company, another range and a day outside it or twice', () => {
    expect(decodeList(list({ companyId: OTHER_COMPANY }))).toBeNull()
    expect(decodeList(list({ to: '2026-11-14' }))).toBeNull()
    expect(decodeList(list({ days: [day({ date: '2026-11-14' })] }))).toBeNull()
    expect(decodeList(list({ days: [day(), day()] }))).toBeNull()
  })

  it('refuses a rate on a day without a published bulletin, and record rates on a non-published record', () => {
    expect(decodeList(list({ days: [day({ bulletinState: 'NoBulletin' })] }))).toBeNull()
    expect(decodeList(list({ days: [day({ rates: [], records: [record({ state: 'NoBulletin' })], bulletinState: 'NoBulletin' })] }))).toBeNull()
    expect(
      decodeList(list({ days: [day({ rates: [], bulletinState: 'NoBulletin', records: [record({ state: 'NoBulletin', rates: [] })] })] })),
    ).not.toBeNull()
  })

  it('accepts a day without records only as NotRecorded with version "0"', () => {
    expect(decodeList(list({ days: [{ ...EMPTY_DAY, version: '1' }] }))).toBeNull()
    expect(decodeList(list({ days: [{ ...EMPTY_DAY, bulletinState: 'NotFound' }] }))).toBeNull()
    expect(decodeList(list({ days: [day({ version: 'x' })] }))).toBeNull()
    expect(decodeList(list({ days: [day({ records: [record({ recordVersion: '0' })] })] }))).toBeNull()
  })

  it('refuses off-list states, provenances, currencies and rates', () => {
    expect(decodeList(list({ days: [day({ bulletinState: 'Maybe' })] }))).toBeNull()
    expect(decodeList(list({ days: [day({ halfDayProvenance: 'Guess' })] }))).toBeNull()
    expect(decodeList(list({ days: [day({ records: [record({ state: 'NotRecorded' })] })] }))).toBeNull()
    const value = (overrides: Record<string, unknown>) => [
      { currencyCode: 'USD', sellingRate: '34.1234', provenance: 'Official', recordId: RECORD, recordVersion: '1', ...overrides },
    ]
    expect(decodeList(list({ days: [day({ rates: value({ currencyCode: 'TRY' }) })] }))).toBeNull()
    expect(decodeList(list({ days: [day({ rates: value({ sellingRate: 34.1234 }) })] }))).toBeNull()
    expect(decodeList(list({ days: [day({ rates: value({ sellingRate: '0' }) })] }))).toBeNull()
  })
})

describe('decodeImportSummary', () => {
  it('decodes the outcomes of the requested range only', () => {
    const summary = { days: [{ date: TO, bulletinOutcome: 'Recorded', halfDayOutcome: 'SourceDisabled' }] }
    expect(decodeImportSummary(summary, FROM, TO)?.days[0].halfDayOutcome).toBe('SourceDisabled')
    expect(decodeImportSummary({ days: [{ ...summary.days[0], date: '2026-11-14' }] }, FROM, TO)).toBeNull()
    expect(decodeImportSummary({ days: [{ ...summary.days[0], bulletinOutcome: 'Done' }] }, FROM, TO)).toBeNull()
    expect(decodeImportSummary({ ...summary, extra: 1 }, FROM, TO)).toBeNull()
  })
})

describe('decodeResolution (kur yoksa işlem durur)', () => {
  const base = {
    rateDay: TO,
    sourceCurrencyId: SOURCE,
    targetCurrencyId: TARGET,
    currencyCode: 'USD',
    source: 'Tcmb',
    halfDayRuleApplied: false,
    previousFullBusinessDay: null,
  }
  const resolved = {
    ...base,
    state: 'Resolved',
    bulletinDate: '2026-11-12',
    sellingRate: '34.1234',
    provenance: 'Official',
    recordId: RECORD,
    recordVersion: '1',
    code: null,
    stoppedAt: null,
  }
  const unavailable = {
    ...base,
    state: 'Unavailable',
    bulletinDate: null,
    sellingRate: null,
    provenance: null,
    recordId: null,
    recordVersion: null,
    code: 'exchange_rate_day_unconfirmed',
    stoppedAt: '2026-11-12',
  }

  it('decodes a resolved rate and an unavailable answer without one', () => {
    expect(decodeResolution(resolved, TO, 'USD')).toMatchObject({ state: 'Resolved', sellingRate: '34.1234' })
    const none = decodeResolution(unavailable, TO, 'USD')
    expect(none).toMatchObject({ state: 'Unavailable', code: 'exchange_rate_day_unconfirmed' })
    expect(none !== null && 'sellingRate' in none).toBe(false)
  })

  it('refuses an Unavailable answer that carries any rate member, or no code', () => {
    for (const member of ['bulletinDate', 'sellingRate', 'provenance', 'recordId', 'recordVersion'] as const) {
      expect(decodeResolution({ ...unavailable, [member]: resolved[member] }, TO, 'USD')).toBeNull()
    }
    expect(decodeResolution({ ...unavailable, code: null }, TO, 'USD')).toBeNull()
    expect(decodeResolution({ ...unavailable, code: ' ' }, TO, 'USD')).toBeNull()
  })

  it('refuses a Resolved answer with a missing rate or a code', () => {
    expect(decodeResolution({ ...resolved, sellingRate: null }, TO, 'USD')).toBeNull()
    expect(decodeResolution({ ...resolved, code: 'exchange_rate_day_unconfirmed' }, TO, 'USD')).toBeNull()
    expect(decodeResolution({ ...resolved, stoppedAt: TO }, TO, 'USD')).toBeNull()
  })

  it('refuses another day, another currency, another source, an unknown state or member set', () => {
    expect(decodeResolution(resolved, '2026-11-12', 'USD')).toBeNull()
    expect(decodeResolution(resolved, TO, 'EUR')).toBeNull()
    expect(decodeResolution({ ...resolved, source: 'Other' }, TO, 'USD')).toBeNull()
    expect(decodeResolution({ ...resolved, state: 'Fallback' }, TO, 'USD')).toBeNull()
    expect(decodeResolution({ ...resolved, extra: 1 }, TO, 'USD')).toBeNull()
    expect(decodeResolution({ ...resolved, currencyCode: null }, TO, 'USD')).toMatchObject({ currencyCode: null })
  })
})

describe('helpers', () => {
  it('accepts only plain positive decimal text within the server limits as a rate', () => {
    expect(isRateText('34.1234')).toBe(true)
    expect(isRateText('0.0001')).toBe(true)
    expect(isRateText('1'.repeat(18))).toBe(true)
    expect(isRateText(`1.${'1'.repeat(10)}`)).toBe(true)
    for (const invalid of ['0', '0.0', '-1', '34,5', '1e3', '01', '.5', '1.', '1'.repeat(19), `1.${'1'.repeat(11)}`, '']) {
      expect(isRateText(invalid), invalid).toBe(false)
    }
    expect(isRateText(34.5)).toBe(false)
  })

  it('accepts three-letter foreign currencies only and normalizes the input', () => {
    expect(isForeignCurrency('USD')).toBe(true)
    expect(isForeignCurrency('TRY')).toBe(false)
    expect(isForeignCurrency('usd')).toBe(false)
    expect(normalizeCurrency(' usd ')).toBe('USD')
  })

  it('counts and shifts calendar days across month and year ends', () => {
    expect(inclusiveDayCount('2026-11-13', '2026-11-13')).toBe(1)
    expect(inclusiveDayCount('2026-12-31', '2027-01-01')).toBe(2)
    expect(inclusiveDayCount('2026-11-14', '2026-11-13')).toBe(0)
    expect(addDays('2026-11-01', -1)).toBe('2026-10-31')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
  })

  it('takes today in Türkiye, not in UTC', () => {
    expect(todayInIstanbul(new Date('2026-11-14T20:59:00Z'))).toBe('2026-11-14')
    expect(todayInIstanbul(new Date('2026-11-14T21:00:00Z'))).toBe('2026-11-15')
  })
})

import { describe, expect, it } from 'vitest'
import {
  TRADE_NAME_MAX,
  TRADE_NAME_TEXT,
  checkTradeName,
  decodeTradeNameView,
  tradeNamePath,
  tradeNameProblemCodeOf,
} from './tradeName'

const PARTY = '11111111-1111-1111-1111-111111111111'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const expected = { partyId: PARTY, companyId: COMPANY }

const raw = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  version: '7',
  legalName: 'Örnek Ticaret A.Ş.',
  tradeName: null,
  viewer: { canUpdateTradeName: false },
  ...over,
})

describe('F-027 trade name contract', () => {
  it('decodes the exact shape, with or without a trade name', () => {
    expect(decodeTradeNameView(raw(), expected)).toEqual(raw())
    expect(decodeTradeNameView(raw({ tradeName: 'Örnek' }), expected)?.tradeName).toBe('Örnek')
  })

  it('refuses an unknown or missing key, a numeric version and a record for another party or company', () => {
    expect(decodeTradeNameView({ ...raw(), extra: true }, expected)).toBeNull()
    const { tradeName: _omitted, ...missing } = raw()
    expect(decodeTradeNameView(missing, expected)).toBeNull()
    expect(decodeTradeNameView(raw({ viewer: { canUpdateTradeName: true, canApprove: true } }), expected)).toBeNull()
    expect(decodeTradeNameView(raw({ version: 7 }), expected)).toBeNull()
    expect(decodeTradeNameView(raw({ version: '9223372036854775808' }), expected)).toBeNull()
    expect(decodeTradeNameView(raw({ partyId: '22222222-2222-2222-2222-222222222222' }), expected)).toBeNull()
    expect(decodeTradeNameView(raw({ companyId: 'bbbbbbbb-0000-0000-0000-000000000002' }), expected)).toBeNull()
  })

  it('refuses names past the server limits or blank', () => {
    expect(decodeTradeNameView(raw({ tradeName: 'x'.repeat(TRADE_NAME_MAX + 1) }), expected)).toBeNull()
    expect(decodeTradeNameView(raw({ tradeName: '   ' }), expected)).toBeNull()
    expect(decodeTradeNameView(raw({ legalName: '' }), expected)).toBeNull()
    expect(decodeTradeNameView(raw({ legalName: 'x'.repeat(251) }), expected)).toBeNull()
  })

  it('mirrors the server normalization: trim, blank clears, at most 200', () => {
    expect(checkTradeName('  Örnek  ')).toEqual({ ok: true, value: 'Örnek' })
    expect(checkTradeName('   ')).toEqual({ ok: true, value: null })
    expect(checkTradeName('x'.repeat(TRADE_NAME_MAX))).toEqual({ ok: true, value: 'x'.repeat(TRADE_NAME_MAX) })
    expect(checkTradeName('x'.repeat(TRADE_NAME_MAX + 1))).toEqual({ ok: false, message: TRADE_NAME_TEXT.tooLong })
  })

  it('reads only the endpoint codes and escapes the path segment', () => {
    expect(tradeNameProblemCodeOf({ code: 'version_conflict' })).toBe('version_conflict')
    expect(tradeNameProblemCodeOf({ code: 'customer_group_exists' })).toBeNull()
    expect(tradeNameProblemCodeOf(null)).toBeNull()
    expect(tradeNamePath('a/b')).toBe('/counterparties/a%2Fb/trade-name')
  })
})

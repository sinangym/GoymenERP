import { describe, expect, it } from 'vitest'
import {
  checkAgainstProfile,
  formatDecimalTextTr,
  fractionDigits,
  isPositiveDecimalText,
  parseAmountInput,
} from './decimalText'

describe('isPositiveDecimalText — wire amount guard', () => {
  it.each(['1', '1000', '1000.5', '1000.50', '0.01', '123456789012345678901234.123'])(
    'accepts canonical positive text %s',
    (value) => {
      expect(isPositiveDecimalText(value)).toBe(true)
    },
  )

  it.each([
    ['zero', '0'],
    ['zero with fraction', '0.000'],
    ['negative', '-1'],
    ['plus sign', '+1'],
    ['exponent', '1e3'],
    ['grouping comma', '1,000'],
    ['grouping dot', '1.000.00'],
    ['leading zero', '01'],
    ['bare fraction', '.5'],
    ['trailing dot', '1.'],
    ['padded', ' 1'],
    ['empty', ''],
  ])('rejects %s (%s)', (_label, value) => {
    expect(isPositiveDecimalText(value)).toBe(false)
  })

  it('rejects a JSON number even when its value is positive', () => {
    expect(isPositiveDecimalText(1000)).toBe(false)
    expect(isPositiveDecimalText(null)).toBe(false)
  })
})

describe('parseAmountInput — Turkish form entry', () => {
  it('turns a comma fraction into wire text digit-for-digit', () => {
    expect(parseAmountInput('1000,50')).toEqual({ ok: true, wire: '1000.50' })
    expect(parseAmountInput('  1000 ')).toEqual({ ok: true, wire: '1000' })
    expect(parseAmountInput('007,10')).toEqual({ ok: true, wire: '7.10' })
  })

  it('refuses a dot instead of guessing whether it groups thousands', () => {
    // In tr-TR "1.000" is one thousand; on the wire it would be one.
    expect(parseAmountInput('1.000')).toEqual({ ok: false, error: 'format' })
    expect(parseAmountInput('1.000,50')).toEqual({ ok: false, error: 'format' })
  })

  it.each(['1 000', '-5', '1e3', '1,2,3', ',5', '5,', 'abc'])('refuses %s', (input) => {
    expect(parseAmountInput(input)).toEqual({ ok: false, error: 'format' })
  })

  it('separates missing from zero', () => {
    expect(parseAmountInput('   ')).toEqual({ ok: false, error: 'required' })
    expect(parseAmountInput('0,00')).toEqual({ ok: false, error: 'not-positive' })
  })
})

describe('precision profile — no client default scale', () => {
  const profile = { profileId: 'p', version: '1', scale: 2 }

  it('keeps recording closed without a profile, however ordinary the amount looks', () => {
    expect(checkAgainstProfile('1000', null)).toEqual({ kind: 'profile-unavailable' })
  })

  it('reports extra digits instead of rounding them', () => {
    expect(fractionDigits('1000.125')).toBe(3)
    expect(checkAgainstProfile('1000.125', profile).kind).toBe('scale-exceeded')
    expect(checkAgainstProfile('1000.12', profile).kind).toBe('within-scale')
    expect(checkAgainstProfile('1000', profile).kind).toBe('within-scale')
  })
})

describe('formatDecimalTextTr — regroups, never rounds or pads', () => {
  it.each([
    ['1', '1'],
    ['1000', '1.000'],
    ['1234567.5', '1.234.567,5'],
    ['1000.00', '1.000,00'],
    ['123456789012345678901234.123', '123.456.789.012.345.678.901.234,123'],
  ])('%s → %s', (wire, shown) => {
    expect(formatDecimalTextTr(wire)).toBe(shown)
  })

  it('will not print a non-canonical value as an amount', () => {
    expect(formatDecimalTextTr('1e3')).toBeNull()
    expect(formatDecimalTextTr('1,000')).toBeNull()
  })
})

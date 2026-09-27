import { describe, expect, it } from 'vitest'
import {
  amountInputFromWire,
  formatDecimalTextTr,
  isNonNegativeDecimalText,
  isPositiveDecimalText,
  parseAmountInput,
  parseNonNegativeAmountInput,
  positiveDifference,
  sameDecimal,
} from './decimalText'

describe('isPositiveDecimalText — server ExactPositiveDecimalV1 grammar', () => {
  it.each(['1', '1000', '1000.50', '0.01', '01', '007.10', '900719925474099312345678901234567890.123'])(
    'accepts %s exactly as the server does',
    (value) => {
      expect(isPositiveDecimalText(value)).toBe(true)
    },
  )

  // The Settings/Finance negative table: 0, -1, 1e3, 1,000, NaN, "", " 1", 0.000.
  it.each(['0', '-1', '1e3', '1,000', 'NaN', '', ' 1', '0.000', '+1', '.5', '1.', '1.2.3'])(
    'rejects %j',
    (value) => {
      expect(isPositiveDecimalText(value)).toBe(false)
    },
  )

  it('rejects a JSON number even when its value is positive', () => {
    expect(isPositiveDecimalText(1000)).toBe(false)
  })
})

describe('isNonNegativeDecimalText — unallocated amount', () => {
  it('allows zero, which a source with nothing left legitimately has', () => {
    expect(isNonNegativeDecimalText('0')).toBe(true)
    expect(isNonNegativeDecimalText('0.00')).toBe(true)
    expect(isNonNegativeDecimalText('-0')).toBe(false)
    expect(isNonNegativeDecimalText(0)).toBe(false)
  })
})

describe('parseAmountInput — Turkish form entry', () => {
  it('turns a comma fraction into wire text and otherwise keeps the digits as typed', () => {
    expect(parseAmountInput('1000,50')).toEqual({ ok: true, wire: '1000.50' })
    expect(parseAmountInput('  1000 ')).toEqual({ ok: true, wire: '1000' })
    expect(parseAmountInput('007,10')).toEqual({ ok: true, wire: '007.10' })
  })

  it('refuses a dot instead of guessing whether it groups thousands', () => {
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

  it('round-trips an existing draft amount back into the input', () => {
    expect(amountInputFromWire('1000.50')).toBe('1000,50')
    expect(parseAmountInput(amountInputFromWire('007.10'))).toEqual({ ok: true, wire: '007.10' })
  })
})

describe('formatDecimalTextTr — regroups, never rounds or pads', () => {
  it.each([
    ['1', '1'],
    ['0', '0'],
    ['1000', '1.000'],
    ['1234567.5', '1.234.567,5'],
    ['1000.00', '1.000,00'],
  ])('%s → %s', (wire, shown) => {
    expect(formatDecimalTextTr(wire)).toBe(shown)
  })

  it('will not print a non-grammar value as an amount', () => {
    expect(formatDecimalTextTr('1e3')).toBeNull()
    expect(formatDecimalTextTr('1,000')).toBeNull()
  })
})

describe('DEC-FIN-014 exact POS arithmetic — BigInt only, never rounded', () => {
  it.each([
    ['1000.00', '25.50', '974.50'],
    ['1000', '25.5', '974.5'],
    ['1000', '0', '1000'],
    ['0.10', '0.09', '0.01'],
    ['900719925474099312345678901234567890.12', '0.01', '900719925474099312345678901234567890.11'],
  ])('%s minus %s is %s', (a, b, expected) => expect(positiveDifference(a, b)).toBe(expected))

  it.each([
    ['1000', '1000'],
    ['1000', '1000.01'],
    ['1000', '1,5'],
    ['x', '1'],
  ])('no positive difference for %s minus %s', (a, b) => expect(positiveDifference(a, b)).toBeNull())

  it('compares exactly across scales', () => {
    expect(sameDecimal('974.5', '974.50')).toBe(true)
    expect(sameDecimal('974.5', '974.51')).toBe(false)
    expect(sameDecimal('974.5', 'x')).toBe(false)
  })

  it('a commission may be zero but keeps the tr-TR input grammar', () => {
    expect(parseNonNegativeAmountInput('0')).toEqual({ ok: true, wire: '0' })
    expect(parseNonNegativeAmountInput('25,50')).toEqual({ ok: true, wire: '25.50' })
    expect(parseNonNegativeAmountInput('1.000')).toEqual({ ok: false, error: 'format' })
    expect(parseNonNegativeAmountInput(' ')).toEqual({ ok: false, error: 'required' })
  })
})

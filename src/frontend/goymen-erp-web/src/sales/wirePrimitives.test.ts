import { describe, expect, it } from 'vitest'
import {
  isDateOnly,
  isPositiveDecimalString,
  isReferenceId,
  isVersionString,
  parseQuantityInput,
} from './wirePrimitives'

describe('isDateOnly', () => {
  it('accepts real calendar days only', () => {
    expect(isDateOnly('2026-09-13')).toBe(true)
    expect(isDateOnly('2028-02-29')).toBe(true)
    expect(isDateOnly('2026-02-29')).toBe(false)
    expect(isDateOnly('2026-04-31')).toBe(false)
    expect(isDateOnly('2026-13-01')).toBe(false)
    expect(isDateOnly('2026-00-10')).toBe(false)
  })

  it('refuses anything that is not exactly YYYY-MM-DD', () => {
    // A UTC timestamp is the record time, a different fact from the business date.
    expect(isDateOnly('2026-09-13T00:00:00Z')).toBe(false)
    expect(isDateOnly('2026-9-13')).toBe(false)
    expect(isDateOnly('13.09.2026')).toBe(false)
    expect(isDateOnly(20260913)).toBe(false)
    expect(isDateOnly(null)).toBe(false)
  })
})

describe('parseQuantityInput', () => {
  it('canonicalizes without passing through a JS number', () => {
    expect(parseQuantityInput('1000')).toEqual({ ok: true, value: '1000' })
    expect(parseQuantityInput(' 1000,5 ')).toEqual({ ok: true, value: '1000.5' })
    expect(parseQuantityInput('1000.50')).toEqual({ ok: true, value: '1000.5' })
    expect(parseQuantityInput('1000,0')).toEqual({ ok: true, value: '1000' })
    expect(parseQuantityInput('007')).toEqual({ ok: true, value: '7' })
    expect(parseQuantityInput('0,25')).toEqual({ ok: true, value: '0.25' })
    // 20 integer digits plus 9 fraction digits: a double would round this.
    expect(parseQuantityInput('12345678901234567890,123456789')).toEqual({
      ok: true,
      value: '12345678901234567890.123456789',
    })
  })

  it('refuses the input whose meaning depends on the reader', () => {
    // One thousand to a Turkish reader, one to the wire. Never guessed.
    expect(parseQuantityInput('1.000')).toEqual({ ok: false, error: 'ambiguous-separator' })
    expect(parseQuantityInput('12.500')).toEqual({ ok: false, error: 'ambiguous-separator' })
    // A comma is unambiguous in this field, and four fraction digits cannot be thousands.
    expect(parseQuantityInput('1,000')).toEqual({ ok: true, value: '1' })
    expect(parseQuantityInput('1.0005')).toEqual({ ok: true, value: '1.0005' })
  })

  it('names why an input is refused', () => {
    expect(parseQuantityInput('   ')).toEqual({ ok: false, error: 'required' })
    expect(parseQuantityInput('0')).toEqual({ ok: false, error: 'not-positive' })
    expect(parseQuantityInput('0,000')).toEqual({ ok: false, error: 'not-positive' })
    for (const bad of ['-1', '+1', '1e3', '1 000', '1.000,5', '1,2,3', 'Infinity', 'NaN', '٣']) {
      expect(parseQuantityInput(bad)).toEqual({ ok: false, error: 'invalid' })
    }
  })
})

describe('isPositiveDecimalString', () => {
  it('accepts only the canonical form the client itself sends', () => {
    expect(isPositiveDecimalString('1000')).toBe(true)
    expect(isPositiveDecimalString('0.25')).toBe(true)
    for (const bad of ['0', '0.0', '1000.0', '01', '-1', '1e3', '1,5', '', ' 1']) {
      expect(isPositiveDecimalString(bad)).toBe(false)
    }
    expect(isPositiveDecimalString(1000)).toBe(false)
  })
})

describe('isVersionString', () => {
  it('is a lossless positive integer string, never a JSON number', () => {
    expect(isVersionString('1')).toBe(true)
    expect(isVersionString('9007199254740993')).toBe(true)
    for (const bad of ['0', '01', '1.0', '-1', '', ' 1']) expect(isVersionString(bad)).toBe(false)
    expect(isVersionString(1)).toBe(false)
  })
})

describe('isReferenceId', () => {
  it('accepts a UUID and refuses the empty one', () => {
    expect(isReferenceId('3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d4e')).toBe(true)
    expect(isReferenceId('00000000-0000-0000-0000-000000000000')).toBe(false)
    expect(isReferenceId('SP-2026-1842')).toBe(false)
    expect(isReferenceId(undefined)).toBe(false)
  })
})

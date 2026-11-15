import { describe, expect, it } from 'vitest'
import {
  AUDIT_OUTCOMES,
  REFUSAL_CLASSES,
  decodeRefusal,
  outcomeFromParam,
  outcomeToParam,
} from './auditQueryOutcome'
import { problemLabel } from './auditText'

const detail = (overrides: Record<string, unknown> = {}) =>
  JSON.stringify({ httpStatus: 422, problemCode: 'validation_failed', refusalClass: 'BusinessRule', fieldErrors: [], ...overrides })

const summary = (overrides: Record<string, unknown> = {}) =>
  JSON.stringify({
    count: 3,
    windowStartUtc: '2026-11-01T09:00:00Z',
    windowEndUtc: '2026-11-01T09:05:00Z',
    refusalClass: 'Validation',
    problemCode: 'validation_failed',
    ...overrides,
  })

describe('audit outcome filter (F023-5a/5b)', () => {
  it('mirrors the server outcomes and refusal classes exactly', () => {
    expect(AUDIT_OUTCOMES).toEqual(['Succeeded', 'Refused'])
    expect(REFUSAL_CLASSES).toEqual(['Validation', 'BusinessRule', 'Concurrency', 'Idempotency', 'RequestShape'])
  })

  it('maps the Turkish URL slugs both ways and marks anything else invalid', () => {
    expect(outcomeFromParam(null)).toBe('')
    expect(outcomeFromParam('  ')).toBe('')
    expect(outcomeFromParam('basarili')).toBe('Succeeded')
    expect(outcomeFromParam('reddedilen')).toBe('Refused')
    expect(outcomeFromParam('Reddedilen')).toBe('invalid')
    expect(outcomeFromParam('Succeeded')).toBe('invalid')
    expect(outcomeToParam('Succeeded')).toBe('basarili')
    expect(outcomeToParam('Refused')).toBe('reddedilen')
    expect(outcomeToParam('')).toBeNull()
    expect(outcomeToParam('invalid')).toBeNull()
  })
})

describe('refused-attempt payload decoder', () => {
  it('decodes the exact detail shape the Audit module writes', () => {
    expect(decodeRefusal(detail({ fieldErrors: [{ field: 'lines[2].unitPrice', code: 'required' }] }))).toEqual({
      kind: 'detail',
      httpStatus: 422,
      problemCode: 'validation_failed',
      refusalClass: 'BusinessRule',
      fieldErrors: [{ field: 'lines[2].unitPrice', code: 'required' }],
    })
  })

  it('decodes the F023-4 summary shape', () => {
    expect(decodeRefusal(summary())).toEqual({
      kind: 'summary',
      count: 3,
      windowStartUtc: '2026-11-01T09:00:00Z',
      windowEndUtc: '2026-11-01T09:05:00Z',
      refusalClass: 'Validation',
      problemCode: 'validation_failed',
    })
  })

  it.each([
    ['not json', 'x'],
    ['an array', '[]'],
    ['an empty object', '{}'],
    ['a missing key', JSON.stringify({ httpStatus: 409, problemCode: 'version_conflict', refusalClass: 'Concurrency' })],
    ['an extra key', detail({ value: 'SENTETIK' })],
    ['a 401', detail({ httpStatus: 401 })],
    ['a 403', detail({ httpStatus: 403 })],
    ['a 5xx', detail({ httpStatus: 500 })],
    ['a 3xx', detail({ httpStatus: 399 })],
    ['a fractional status', detail({ httpStatus: 409.5 })],
    ['a string status', detail({ httpStatus: '409' })],
    ['an unknown refusal class', detail({ refusalClass: 'Authorization' })],
    ['a non-canonical problem code', detail({ problemCode: 'Version Conflict' })],
    ['a too long problem code', detail({ problemCode: 'a'.repeat(129) })],
    ['field errors that are not a list', detail({ fieldErrors: {} })],
    ['too many field errors', detail({ fieldErrors: Array.from({ length: 65 }, () => ({ field: 'name', code: 'required' })) })],
    ['a field error with an extra key', detail({ fieldErrors: [{ field: 'name', code: 'required', value: 'x' }] })],
    ['a field error with a bad field name', detail({ fieldErrors: [{ field: '0name', code: 'required' }] })],
    ['a field error with a bad code', detail({ fieldErrors: [{ field: 'name', code: 'Required' }] })],
    ['a summary with zero count', summary({ count: 0 })],
    ['a summary with a fractional count', summary({ count: 1.5 })],
    ['a summary with a local instant', summary({ windowStartUtc: '2026-11-01T09:00:00' })],
    ['a summary whose window ends before it starts', summary({ windowEndUtc: '2026-11-01T08:00:00Z' })],
    ['a summary with an unknown class', summary({ refusalClass: 'Security' })],
  ])('fails closed on %s', (_name, afterJson) => {
    expect(decodeRefusal(afterJson)).toBeNull()
  })
})

describe('problem code labels', () => {
  it('labels the common refusal codes and leaves an unknown code to be shown raw', () => {
    expect(problemLabel('version_conflict')).toBe('Sürüm çakışması')
    expect(problemLabel('validation_failed')).toBe('Doğrulama hatası')
    expect(problemLabel('purchasing_request_not_draft')).toBeNull()
    expect(problemLabel('toString')).toBeNull()
  })
})

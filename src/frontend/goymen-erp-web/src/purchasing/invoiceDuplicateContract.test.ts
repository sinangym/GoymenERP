/** WP15 (1.16): strict decoder of the incoming invoice duplicate warning. Synthetic ids (SENTETIK). */
import { describe, expect, it } from 'vitest'
import { decodeInvoiceDuplicateCheck } from './invoiceDuplicateContract'

const SELF = '15000000-0000-4000-8000-000000000001'
const OTHER = '15000000-0000-4000-8000-000000000002'
const warning = { invoiceId: OTHER, code: 'purchasing.invoice-duplicate.same-ettn', officialNumber: 'SENTETIK-1', invoiceDate: '2026-11-20',
  link: `/satinalma/faturalar/${OTHER}` }
const answer = (overrides: Record<string, unknown> = {}) => ({ behavior: 'warn', warnings: [warning], truncated: false, ...overrides })

describe('decodeInvoiceDuplicateCheck', () => {
  it('accepts a warning answer and an empty one', () => {
    expect(decodeInvoiceDuplicateCheck(answer(), SELF)?.warnings).toHaveLength(1)
    expect(decodeInvoiceDuplicateCheck(answer({ warnings: [] }), SELF)?.warnings).toEqual([])
    expect(decodeInvoiceDuplicateCheck(answer({ warnings: [{ ...warning, invoiceDate: null }] }), SELF)?.warnings[0].invoiceDate).toBeNull()
  })

  it.each([
    ['a blocking behavior', answer({ behavior: 'block' })],
    ['an extra member', answer({ extra: 1 })],
    ['the invoice itself', answer({ warnings: [{ ...warning, invoiceId: SELF, link: `/satinalma/faturalar/${SELF}` }] })],
    ['an unknown code', answer({ warnings: [{ ...warning, code: 'purchasing.invoice-duplicate.other' }] })],
    ['a foreign link', answer({ warnings: [{ ...warning, link: 'https://example.invalid/x' }] })],
    ['an impossible date', answer({ warnings: [{ ...warning, invoiceDate: '2026-02-30' }] })],
    ['a repeated invoice', answer({ warnings: [warning, warning] })],
    ['an empty number', answer({ warnings: [{ ...warning, officialNumber: ' ' }] })],
    ['the empty id', answer({ warnings: [{ ...warning, invoiceId: '00000000-0000-0000-0000-000000000000', link: '/satinalma/faturalar/00000000-0000-0000-0000-000000000000' }] })],
  ])('refuses %s', (_, raw) => {
    expect(decodeInvoiceDuplicateCheck(raw, SELF)).toBeNull()
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchIncomingInvoice, fetchIncomingInvoices, submitInvoiceCommand } from './incomingInvoiceApi'
import { incomingInvoiceFixture, invoiceTestId as id } from './incomingInvoiceFixtures.testSupport'
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
afterEach(() => vi.unstubAllGlobals())
describe('Incoming invoice transport (synthetic, NOT_ENV)', () => {
  it('sends selection and expected version only with company and the supplied idempotency key', async () => {
    const invoice = incomingInvoiceFixture(), command = { action: 'matching' as const, request: { expectedVersion: '1', receipts: [
      { invoiceLineId: invoice.lines[0].id, purchaseOrderId: id(11), acceptanceId: id(13), quantity: '2' }] } }
    const fetch = vi.fn().mockResolvedValue(response({ invoice, isReplay: false, originalCorrelationId: 'synthetic', financeOutcome: null }))
    vi.stubGlobal('fetch', fetch)
    expect((await submitInvoiceCommand(invoice.companyId, invoice.id, command, 'synthetic-key')).type).toBe('applied')
    const [path, init] = fetch.mock.calls[0]
    expect(path).toContain(`/purchasing/invoices/${invoice.id}/matching`)
    expect(init.headers).toMatchObject({ 'X-Company-Id': invoice.companyId, 'Idempotency-Key': 'synthetic-key' })
    expect(JSON.parse(init.body)).toEqual(command.request)
    expect(init.body).not.toMatch(/expectedNet|controlRate|policy|tolerance|companyId/)
  })
  it.each([401, 403, 404, 409, 503])('keeps read HTTP %s distinct', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({}, status)))
    expect(await fetchIncomingInvoice(id(2), id(1), new AbortController().signal)).toEqual({ type: 'http', status })
  })
  it('keeps malformed successful writes unresolved and never retries automatically', async () => {
    const fetch = vi.fn().mockResolvedValue(response({ status: 'saved' }))
    vi.stubGlobal('fetch', fetch)
    expect(await submitInvoiceCommand(id(2), id(1), { action: 'finalization', request: { expectedVersion: '2' } }, 'same-key'))
      .toEqual({ type: 'unknown', reason: 'malformed' })
    expect(fetch).toHaveBeenCalledTimes(1)
  })
  it('encodes official-number search and rejects a foreign company in a successful page', async () => {
    const fetch = vi.fn().mockResolvedValue(response({ pageIndex: 0, hasNext: false, items: [incomingInvoiceFixture()] }))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchIncomingInvoices(id(99), 'A&B', 0, new AbortController().signal)).toEqual({ type: 'malformed' })
    expect(fetch.mock.calls[0][0]).toContain('officialNumber=A%26B')
    expect(fetch.mock.calls[0][1].cache).toBe('no-store')
  })
  it.each([201, 202, 204])('does not accept a source invoice under unexpected HTTP %s', async status => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(status === 204 ? null : JSON.stringify(incomingInvoiceFixture()), { status })))
    expect(await fetchIncomingInvoice(id(2), id(1), new AbortController().signal)).toEqual({ type: 'malformed' })
  })

})

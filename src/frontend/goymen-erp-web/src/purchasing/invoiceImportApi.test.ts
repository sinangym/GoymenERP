import { afterEach, describe, expect, it, vi } from 'vitest'
import { previewInvoiceImport, reserveInvoiceImport, fetchInvoiceImports } from './invoiceImportApi'
import { importPreviewFixture, importRequestFixture, importReservationFixture, importTestId as id } from './invoiceImportFixtures.testSupport'
const response = (body: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(body), { status,
  headers: { 'Content-Type': 'application/json', ...headers } })
afterEach(() => vi.unstubAllGlobals())
describe('Purchase invoice file transport (synthetic, NOT_ENV)', () => {
  it('reopens listed reservations through a no-store company-bound read', async () => {
    const page = { companyId: id(2), pageIndex: 1, hasNext: false, items: [importReservationFixture().reservation] }
    const fetch = vi.fn().mockResolvedValue(response(page)); vi.stubGlobal('fetch', fetch)
    expect(await fetchInvoiceImports(id(2), 1, new AbortController().signal)).toEqual({ type: 'ok-single', data: page })
    expect(fetch.mock.calls[0][0]).toContain('/invoice-imports?pageIndex=1')
    expect(fetch.mock.calls[0][1]).toMatchObject({ cache: 'no-store', headers: expect.objectContaining({ 'X-Company-Id': id(2) }) })
    expect(fetch.mock.calls[0][1].method).toBeUndefined()
  })
  it('reserves once with a frozen company and key and no caller-selected actor', async () => {
    const fetch = vi.fn().mockResolvedValue(response(importReservationFixture())); vi.stubGlobal('fetch', fetch)
    expect((await reserveInvoiceImport(id(2), 'same-key')).type).toBe('applied')
    const [path, init] = fetch.mock.calls[0]
    expect(path).toContain('/invoice-imports/reservations')
    expect(init.headers).toMatchObject({ 'X-Company-Id': id(2), 'Idempotency-Key': 'same-key' })
    expect(init.body).toBe('{}'); expect(fetch).toHaveBeenCalledTimes(1)
  })
  it('keeps a malformed successful reservation unknown instead of generating another key', async () => {
    const fetch = vi.fn().mockResolvedValue(response({ accepted: true })); vi.stubGlobal('fetch', fetch)
    expect(await reserveInvoiceImport(id(2), 'same-key')).toEqual({ type: 'unknown', reason: 'malformed' })
    expect(fetch).toHaveBeenCalledTimes(1)
  })
  it('sends the selected document version and hash without a raw file, money calculation or idempotency write', async () => {
    const fetch = vi.fn().mockResolvedValue(response(importPreviewFixture())); vi.stubGlobal('fetch', fetch)
    const request = importRequestFixture(), signal = new AbortController().signal
    expect(await previewInvoiceImport(id(2), id(1), request, signal)).toEqual({ type: 'ok-single', data: importPreviewFixture() })
    const [, init] = fetch.mock.calls[0]
    expect(JSON.parse(init.body)).toEqual(request); expect(init.headers['Idempotency-Key']).toBeUndefined()
    expect(init.cache).toBe('no-store'); expect(init.signal).toBe(signal)
  })
  it.each([401, 403, 404, 409, 503])('keeps source-read HTTP %s distinct', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({}, status)))
    expect(await previewInvoiceImport(id(2), id(1), importRequestFixture(), new AbortController().signal)).toEqual({ type: 'http', status })
  })
  it('rejects a successful foreign-company document response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ ...importPreviewFixture(), companyId: id(99) })))
    expect(await previewInvoiceImport(id(2), id(1), importRequestFixture(), new AbortController().signal)).toEqual({ type: 'malformed' })
  })
  it.each([201, 202, 204])('keeps HTTP %s outside the exact reservation and preview contracts', async status => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(status === 204 ? null : JSON.stringify(importReservationFixture()), { status })))
    expect(await reserveInvoiceImport(id(2), 'same-key')).toEqual({ type: 'unknown', reason: 'malformed' })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(status === 204 ? null : JSON.stringify(importPreviewFixture()), { status })))
    expect(await previewInvoiceImport(id(2), id(1), importRequestFixture(), new AbortController().signal)).toEqual({ type: 'malformed' })
    expect(await fetchInvoiceImports(id(2), 0, new AbortController().signal)).toEqual({ type: 'malformed' })
  })

})

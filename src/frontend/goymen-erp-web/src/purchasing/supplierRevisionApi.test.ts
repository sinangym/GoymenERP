import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearSupplierRevisionAttempt, decodeSupplierRevisionHistory, fetchSupplierRevisions, readSupplierRevisionAttempt,
  saveSupplierRevisionAttempt, sendSupplierRevision, supplierRevisionAttemptKey, type SupplierRevisionAttempt } from './supplierRevisionApi'

const company = '11111111-1111-4111-8111-111111111111'; const order = '22222222-2222-4222-8222-222222222222'
const line = '33333333-3333-4333-8333-333333333333'; const supplier = '44444444-4444-4444-8444-444444444444'
const account = '55555555-5555-4555-8555-555555555555'; const unit = '66666666-6666-4666-8666-666666666666'
const previous = '77777777-7777-4777-8777-777777777777'; const at = '2026-11-17T09:00:00Z'
const attempt: SupplierRevisionAttempt = { actorId: 'SENTETIK', companyId: company, orderId: order, idempotencyKey: 'SENTETIK-key',
  command: { kind: 'supplier', expectedVersion: '3', supplierPartyId: supplier, companyAccountId: account, reason: 'SENTETIK reason' } }
function history() { return { companyId: company, orderId: order, version: '4', revision: 1,
  revisions: [{ revision: 1, orderVersionAfter: '4', previousSupplierPartyId: previous, supplierPartyId: supplier, preparedBy: 'SENTETIK',
    previousApprovedBy: 'SENTETIK-checker', previousApprovedAtUtc: at, reason: 'SENTETIK reason', recordedAtUtc: at }] } }
function answer() { return { detail: { order: { id: order, companyId: company, status: 'Draft', version: '4', supplierPartyId: supplier,
  companyAccountId: account, sourceRequestId: null, effectiveOn: '2026-11-17', createdAtUtc: at, updatedAtUtc: at, lineCount: 1 },
  lines: [{ lineId: line, lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null, productId: previous,
    productVersion: '1', unitId: unit, ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10', cancelled: '0' }],
  acceptances: [], cancellation: null }, isReplay: false, originalCorrelationId: 'SENTETIK-correlation' } }
function response(body: unknown, status = 200, headers: Record<string, string> = {}) { return new Response(JSON.stringify(body), { status, headers }) }
beforeEach(() => sessionStorage.clear())
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('ALS-D112/D115 supplier revision wire and pending attempt', () => {
  it('posts the exact supplier, account, version, reason and key without a client company body', async () => {
    const fetcher = vi.fn(async () => response(answer())); vi.stubGlobal('fetch', fetcher)
    expect(await sendSupplierRevision(attempt)).toEqual({ type: 'applied' })
    const [url, init] = fetcher.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toContain(`/orders/${order}/supplier-revisions`)
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe(attempt.idempotencyKey)
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(company)
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: '3', supplierPartyId: supplier, companyAccountId: account, reason: 'SENTETIK reason' })
  })
  it.each(['company', 'order', 'version', 'supplier', 'account', 'replay'])('does not trust a malformed %s command answer', async fault => {
    const body = answer()
    if (fault === 'company') body.detail.order.companyId = previous
    if (fault === 'order') body.detail.order.id = previous
    if (fault === 'version') body.detail.order.version = '3'
    if (fault === 'supplier') body.detail.order.supplierPartyId = previous
    if (fault === 'account') body.detail.order.companyAccountId = previous
    if (fault === 'replay') body.isReplay = true
    vi.stubGlobal('fetch', vi.fn(async () => response(body)))
    expect(await sendSupplierRevision(attempt)).toEqual({ type: 'unknown' })
  })
  it.each([400, 401, 403, 404, 409, 415, 422])('reports definite refusal %s without treating it as saved', async status => {
    vi.stubGlobal('fetch', vi.fn(async () => response({ code: 'purchasing_supplier_revision_blocked' }, status)))
    expect(await sendSupplierRevision(attempt)).toEqual({ type: 'failed', status, code: 'purchasing_supplier_revision_blocked' })
  })
  it.each([500, 503, 204])('keeps ambiguous status %s unknown', async status => {
    vi.stubGlobal('fetch', vi.fn(async () => status === 204 ? new Response(null, { status }) : response({}, status)))
    expect(await sendSupplierRevision(attempt)).toEqual({ type: 'unknown' })
  })
  it('keeps network loss unknown and preserves the same body and key for retry', async () => {
    const key = supplierRevisionAttemptKey(1, attempt.actorId, company, order); saveSupplierRevisionAttempt(key, attempt)
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK offline') }))
    expect(await sendSupplierRevision(attempt)).toEqual({ type: 'unknown' })
    expect(readSupplierRevisionAttempt(key, attempt.actorId, company, order)).toEqual(attempt)
    expect(() => readSupplierRevisionAttempt(key, 'SENTETIK-other', company, order)).toThrow()
    clearSupplierRevisionAttempt(key, { ...attempt, idempotencyKey: 'different' })
    expect(sessionStorage.getItem(key)).not.toBeNull()
    clearSupplierRevisionAttempt(key, attempt); expect(sessionStorage.getItem(key)).toBeNull()
  })
  it('fails closed for corrupted pending storage and for storage write failure', () => {
    const key = supplierRevisionAttemptKey(1, attempt.actorId, company, order); sessionStorage.setItem(key, '{')
    expect(() => readSupplierRevisionAttempt(key, attempt.actorId, company, order)).toThrow()
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('SENTETIK storage denied') })
    expect(() => saveSupplierRevisionAttempt(key, attempt)).toThrow()
  })
  it('validates source scope and the strictly ascending revision chain', () => {
    expect(decodeSupplierRevisionHistory(history(), company, order)).not.toBeNull()
    expect(decodeSupplierRevisionHistory(history(), previous, order)).toBeNull()
    const bad = history(); bad.revisions[0].orderVersionAfter = '5'
    expect(decodeSupplierRevisionHistory(bad, company, order)).toBeNull()
    expect(decodeSupplierRevisionHistory({ ...history(), revision: 2 }, company, order)).toBeNull()
  })
  it.each([{ unitPrice: 12 }, { unitPrice: '12', deferralAmount: '-1' }, { unitPrice: '12', priceUnitId: 'bad' },
    { unitPrice: '12', companyId: company }])('refuses corrupted price attempts before retry %j', price => {
    const key = supplierRevisionAttemptKey(1, attempt.actorId, company, order)
    sessionStorage.setItem(key, JSON.stringify({ ...attempt, command: { kind: 'price', expectedVersion: '4', lineId: previous, price } }))
    expect(() => readSupplierRevisionAttempt(key, attempt.actorId, company, order)).toThrow()
  })
  it('rejects a cross-company history and propagates read failures', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => response({ ...history(), companyId: previous })))
    expect(await fetchSupplierRevisions(company, order)).toEqual({ type: 'malformed' })
    vi.stubGlobal('fetch', vi.fn(async () => response({}, 503)))
    expect(await fetchSupplierRevisions(company, order)).toMatchObject({ type: 'http', status: 503 })
  })
})

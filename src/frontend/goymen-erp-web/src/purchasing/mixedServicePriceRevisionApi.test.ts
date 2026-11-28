import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeMixedServicePricePreview, fetchMixedServicePricePreview, mixedPriceVersion, sendMixedServicePriceRevision, type MixedServicePriceRequest } from './mixedServicePriceRevisionApi'
import { decodePurchaseOrderCommandDetail, decodePurchaseOrderCommandResponse, decodePurchaseOrderDetail } from './contract'
import { mixedPriceAnswer, mixedPriceFixture, mixedPriceId as id, mixedPriceWire } from './mixedServicePriceRevisionFixtures.testSupport'
const request = (): MixedServicePriceRequest => ({ expectedVersion: '4', services: mixedPriceFixture().services.map(x => x.selection),
  changes: [{ serviceOrderId: id(2), lines: [{ lineId: id(6), unitPrice: '0' }] }], reason: 'SENTETIK native price reason' })
const response = (body: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(body), { status,
  headers: { 'Content-Type': 'application/json', ...headers } })
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })
describe('mixed price native read and frozen command transport (source tests)', () => {
  it('reuses the exact native command-detail decoder while read/default command contracts keep their existing shapes', () => {
    const parent = mixedPriceWire().parent
    expect(decodePurchaseOrderCommandDetail(parent)).not.toBeNull()
    expect(decodePurchaseOrderDetail(parent)).toBeNull()
    expect(decodePurchaseOrderCommandResponse(mixedPriceAnswer())?.detail.order.status).toBe('Draft')
    expect(decodePurchaseOrderCommandDetail({ ...parent, order: { ...parent.order, supplierDisplay: null } })).toBeNull()
    expect(decodeMixedServicePricePreview(mixedPriceWire(), id(1), id(10), '4')).toEqual(mixedPriceFixture())
  })
  it.each(['company', 'parent', 'status', 'version', 'empty', 'duplicate', 'supplier', 'account', 'currency', 'selection', 'line', 'private', 'metadata'])
    ('rejects unbound %s human facts', fault => {
      const v = mixedPriceWire(), first = v.services[0]
      const broken: unknown = fault === 'company' ? { ...v, companyId: id(99) }
        : fault === 'parent' ? { ...v, parent: { ...v.parent, order: { ...v.parent.order, id: id(99) } } }
        : fault === 'status' ? { ...v, parent: { ...v.parent, order: { ...v.parent.order, status: 'Draft' } } }
        : fault === 'version' ? { ...v, parent: { ...v.parent, order: { ...v.parent.order, version: '5' } } }
        : fault === 'empty' ? { ...v, services: [] } : fault === 'duplicate' ? { ...v, services: [first, first] }
        : fault === 'supplier' ? { ...v, services: [{ ...first, service: { ...first.service, supplierPartyId: id(99) } }] }
        : fault === 'account' ? { ...v, services: [{ ...first, service: { ...first.service, companyAccountId: id(99) } }] }
        : fault === 'currency' ? { ...v, services: [{ ...first, service: { ...first.service, currencyId: id(99) } }] }
        : fault === 'selection' ? { ...v, services: [{ ...first, selection: { ...first.selection, expectedVersion: '3' } }] }
        : fault === 'line' ? { ...v, services: [{ ...first, selection: { ...first.selection, lineIds: [id(99)] } }] }
        : fault === 'private' ? { ...v, receipt: {} } : { ...v, services: [{ ...first, priceRevision: 2147483648 }] }
      expect(decodeMixedServicePricePreview(broken, id(1), id(10), '4')).toBeNull()
    })
  it.each(['0', '01', '-1', '9223372036854775808', '1\n', '1\r', '1\r\n'])('rejects noncanonical Int64 text %s', value => {
    expect(mixedPriceVersion(value)).toBe(false)
    const v = mixedPriceWire(); expect(decodeMixedServicePricePreview({ ...v, services: [{ ...v.services[0], initialPreparedVersion: value }] }, id(1), id(10), '4')).toBeNull()
  })
  it('preserves full Int64 selected identity and excludes only incrementing Max from a new mutation', () => {
    expect(mixedPriceVersion('9007199254740993', true)).toBe(true)
    expect(mixedPriceVersion('9223372036854775807')).toBe(true); expect(mixedPriceVersion('9223372036854775807', true)).toBe(false)
  })
  it('sends only the exact four request fields/key/company header and validates the original N+1 Draft answer', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(response(mixedPriceAnswer())); vi.stubGlobal('fetch', fetcher)
    expect((await sendMixedServicePriceRevision(id(1), id(10), request(), 'SENTETIK-original-key')).type).toBe('applied')
    const [url, init] = fetcher.mock.calls[0]
    expect(String(url)).toContain(`/orders/${id(10)}/service-price-revisions`)
    expect(init?.headers).toMatchObject({ 'X-Company-Id': id(1), 'Idempotency-Key': 'SENTETIK-original-key' })
    expect(init?.body).toBe(JSON.stringify(request()))
    expect(Object.keys(JSON.parse(String(init?.body)))).toEqual(['expectedVersion', 'services', 'changes', 'reason'])
  })
  it.each(['company', 'version', 'state', 'private', 'replay'])('keeps malformed %s success unknown', async fault => {
    const v = mixedPriceAnswer()
    const body = fault === 'company' ? { ...v, detail: { ...v.detail, order: { ...v.detail.order, companyId: id(99) } } }
      : fault === 'version' ? { ...v, detail: { ...v.detail, order: { ...v.detail.order, version: '6' } } }
      : fault === 'state' ? { ...v, detail: { ...v.detail, order: { ...v.detail.order, status: 'Approved' } } }
      : fault === 'private' ? { ...v, receipt: {} } : { ...v, isReplay: true }
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(response(body)))
    expect((await sendMixedServicePriceRevision(id(1), id(10), request(), 'SENTETIK-key')).type).toBe('unknown')
  })
  it.each([401, 403, 503])('retains first status %s because a final authority fence can occur after commit', async status => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(response({}, status)))
    expect(await sendMixedServicePriceRevision(id(1), id(10), request(), 'SENTETIK-key')).toMatchObject({ type: 'unknown', status })
  })
  it('uses the dedicated Approved read path without command metadata and fails closed on foreign payload/203', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(response(mixedPriceWire())); vi.stubGlobal('fetch', fetcher)
    expect((await fetchMixedServicePricePreview(id(1), id(10), '4', new AbortController().signal)).type).toBe('ok-single')
    expect(String(fetcher.mock.calls[0][0])).toContain('/service-price-revision-preview?expectedVersion=4')
    expect(fetcher.mock.calls[0][1]?.cache).toBe('no-store')
    expect(fetcher.mock.calls[0][1]?.headers).not.toHaveProperty('Idempotency-Key')
    fetcher.mockResolvedValue(response(mixedPriceWire(), 203))
    expect((await fetchMixedServicePricePreview(id(1), id(10), '4', new AbortController().signal)).type).toBe('malformed')
  })
})

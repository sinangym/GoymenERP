import { afterEach, describe, expect, it, vi } from 'vitest'
import { recordInvoiceImport } from './invoiceImportAdmissionApi'
import { admissionSnapshot, admissionResult } from './invoiceImportAdmissionFixtures.testSupport'
import { importTestId as id } from './invoiceImportFixtures.testSupport'
afterEach(() => vi.unstubAllGlobals())
describe('source invoice admission transport', () => {
  it('keeps frozen request and key identical after 503 and refuses replay header disagreement', async () => {
    const snapshot = admissionSnapshot(), fetcher = vi.fn(async () => new Response('{}', { status: 503 })); vi.stubGlobal('fetch', fetcher)
    expect((await recordInvoiceImport(id(2), id(1), snapshot, 'SENTETIK-key')).type).toBe('unknown')
    await recordInvoiceImport(id(2), id(1), snapshot, 'SENTETIK-key'); expect(fetcher.mock.calls[1]).toEqual(fetcher.mock.calls[0])
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ...admissionResult(), isReplay: true }), { status: 200 })))
    expect(await recordInvoiceImport(id(2), id(1), snapshot, 'SENTETIK-key')).toEqual({ type: 'unknown', reason: 'malformed' })
  })
  it('accepts the original recorded invoice only with current company and exact original-correlation replay proof', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ...admissionResult(), originalCorrelationId: 'purchase-import-SENTETIK-key' }), { status: 200 })))
    expect((await recordInvoiceImport(id(2), id(1), admissionSnapshot(), 'SENTETIK-key')).type).toBe('applied')
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ...admissionResult(), isReplay: true }), { status: 200,
      headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK' } })))
    expect((await recordInvoiceImport(id(2), id(1), admissionSnapshot(), 'SENTETIK-key')).type).toBe('applied')
  })
  it.each([201, 202, 204])('keeps unexpected admission HTTP %s unresolved with the original request', async status => {
    const snapshot = admissionSnapshot()
    const fetcher = vi.fn(async () => new Response(status === 204 ? null : JSON.stringify({ ...admissionResult(), originalCorrelationId: 'purchase-import-SENTETIK-key' }), { status }))
    vi.stubGlobal('fetch', fetcher)
    expect(await recordInvoiceImport(id(2), id(1), snapshot, 'SENTETIK-key')).toEqual({ type: 'unknown', reason: 'malformed' })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

})

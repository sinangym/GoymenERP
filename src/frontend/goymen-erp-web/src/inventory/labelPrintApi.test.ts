import { afterEach, describe, expect, it, vi } from 'vitest'
import { generateLabelPdf } from './labelPrintApi'

const nodeProcess = (globalThis as unknown as { process: { getBuiltinModule(name: string): unknown } }).process
const { Blob: NativeBlob } = nodeProcess.getBuiltinModule('node:buffer') as { Blob: new(parts: BlobPart[], options?: BlobPropertyBag) => Blob }
function pdfResponse(text: string, headers: Record<string, string>): Response {
  const response = new Response(text, { headers })
  // Match a browser Response's Blob API without a global polyfill; prefix/type validation reads the real bytes.
  Object.defineProperty(response, 'blob', { value: async () => new NativeBlob([text], { type: headers['Content-Type'] }) })
  return response
}

const label = '8a170000-0000-4000-8000-000000000001'
const company = '8a170000-0000-4000-8000-000000000002'
const document = '8a170000-0000-4000-8000-000000000003'
afterEach(() => vi.unstubAllGlobals())

describe('etiket PDF komutu', () => {
  it('SENTETIK yanıt PDF ve belge kaydı taşımadan başarı sayılmaz', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(pdfResponse('%PDF-1.7 SENTETIK', { 'Content-Type': 'application/pdf', 'X-Document-Id': document })))
    const result = await generateLabelPdf(label, '2026-11-16T09:00:00.000Z', company, 'SENTETIK-key')
    expect(result.type).toBe('applied')
    if (result.type !== 'applied') throw new Error('Expected real PDF bytes')
    expect(await result.blob.text()).toBe('%PDF-1.7 SENTETIK')
  })
  it('replay başlıklarının uyuşmaması sonucu bilinmeyen bırakır', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(pdfResponse('%PDF-1.7 SENTETIK', { 'Content-Type': 'application/pdf', 'X-Document-Id': document, 'Idempotency-Replayed': 'true' })))
    expect(await generateLabelPdf(label, '2026-11-16T09:00:00.000Z', company, 'SENTETIK-key'))
      .toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it('503 ve bozuk PDF bir ikinci kaydın başarılı kanıtı sayılmaz', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(null, { status: 503 }))
      .mockResolvedValueOnce(pdfResponse('broken', { 'Content-Type': 'application/pdf', 'X-Document-Id': document }))
    vi.stubGlobal('fetch', fetch)
    expect(await generateLabelPdf(label, '2026-11-16T09:00:00.000Z', company, 'SENTETIK-key')).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    expect(await generateLabelPdf(label, '2026-11-16T09:00:00.000Z', company, 'SENTETIK-key')).toEqual({ type: 'unknown', reason: 'malformed-response' })
    expect(fetch.mock.calls[0]?.[1].headers['Idempotency-Key']).toBe(fetch.mock.calls[1]?.[1].headers['Idempotency-Key'])
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { generateLabelPdf } from './labelPrintApi'

const label = '8a170000-0000-4000-8000-000000000001'
const company = '8a170000-0000-4000-8000-000000000002'
const document = '8a170000-0000-4000-8000-000000000003'
afterEach(() => vi.unstubAllGlobals())

describe('etiket PDF komutu', () => {
  it('SENTETIK yanıt PDF ve belge kaydı taşımadan başarı sayılmaz', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('%PDF-1.7 SENTETIK', {
      headers: { 'Content-Type': 'application/pdf', 'X-Document-Id': document },
    })))
    expect((await generateLabelPdf(label, '2026-11-16T09:00:00.000Z', company, 'SENTETIK-key')).type).toBe('applied')
  })
  it('replay başlıklarının uyuşmaması sonucu bilinmeyen bırakır', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('%PDF-1.7 SENTETIK', {
      headers: { 'Content-Type': 'application/pdf', 'X-Document-Id': document, 'Idempotency-Replayed': 'true' },
    })))
    expect(await generateLabelPdf(label, '2026-11-16T09:00:00.000Z', company, 'SENTETIK-key'))
      .toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it('503 ve bozuk PDF bir ikinci kaydın başarılı kanıtı sayılmaz', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(null, { status: 503 }))
      .mockResolvedValueOnce(new Response('broken', { headers: { 'Content-Type': 'application/pdf', 'X-Document-Id': document } }))
    vi.stubGlobal('fetch', fetch)
    expect((await generateLabelPdf(label, '2026-11-16T09:00:00.000Z', company, 'SENTETIK-key')).type).toBe('unknown')
    expect((await generateLabelPdf(label, '2026-11-16T09:00:00.000Z', company, 'SENTETIK-key')).type).toBe('unknown')
    expect(fetch.mock.calls[0]?.[1].headers['Idempotency-Key']).toBe(fetch.mock.calls[1]?.[1].headers['Idempotency-Key'])
  })
})

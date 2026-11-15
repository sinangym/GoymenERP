import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  fetchInventoryShortageWarnings,
  SHORTAGE_WARNING_INVALID_QUERY,
  shortageWarningProblemCode,
} from './shortageWarningApi'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('fetchInventoryShortageWarnings', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('yalnız sahibin uç noktasını şirket başlığı ve no-store ile okur', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json({ companyId: COMPANY, supplyDates: 'NotAnswered', items: [] }))

    const result = await fetchInventoryShortageWarnings(COMPANY, { onlyWarnings: true })

    expect(result).toEqual({ type: 'ok-single', data: { companyId: COMPANY, supplyDates: 'NotAnswered', items: [] } })
    expect(String(fetchMock.mock.calls[0][0])).toBe('/api/v1/inventory/shortage-warnings?onlyWarnings=true')
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ cache: 'no-store', headers: { 'X-Company-Id': COMPANY } })
  })

  it('tüm ürünler istendiğinde onlyWarnings=false gönderir', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json({ companyId: COMPANY, supplyDates: 'Answered', items: [] }))
    await fetchInventoryShortageWarnings(COMPANY, { onlyWarnings: false })
    expect(String(fetchMock.mock.calls[0][0])).toBe('/api/v1/inventory/shortage-warnings?onlyWarnings=false')
  })

  it('başka şirketin yanıtını bozuk sayar, hata gövdesini kodu ile taşır', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json({ companyId: OTHER_COMPANY, supplyDates: 'Answered', items: [] }))
      .mockResolvedValueOnce(json({ code: SHORTAGE_WARNING_INVALID_QUERY }, 400))
    expect(await fetchInventoryShortageWarnings(COMPANY, { onlyWarnings: true })).toEqual({ type: 'malformed' })
    const problem = await fetchInventoryShortageWarnings(COMPANY, { onlyWarnings: true })
    expect(problem.type === 'http' ? shortageWarningProblemCode(problem.body) : null).toBe(SHORTAGE_WARNING_INVALID_QUERY)
  })

  it('iptal edilen isteği eski yanıt, ağ hatasını ağ hatası olarak döndürür', async () => {
    const controller = new AbortController()
    controller.abort()
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('SENTETIK fetch failed'))
    expect(await fetchInventoryShortageWarnings(COMPANY, { onlyWarnings: true }, controller.signal))
      .toEqual({ type: 'fence-stale' })
    vi.stubGlobal('navigator', { onLine: true })
    expect(await fetchInventoryShortageWarnings(COMPANY, { onlyWarnings: true })).toEqual({ type: 'network-error' })
  })
})

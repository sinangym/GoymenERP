import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeFinanceJournalSources, fetchFinanceJournalSources } from './financeJournalSources'
const company = 'a0000000-0000-4000-8000-000000000001'
export function financeSourcesBody() {
  return { companyId: company, items: [{ eventId: 'a0000000-0000-4000-8000-000000000002', companyId: company,
    sourceId: 'a0000000-0000-4000-8000-000000000003', sourceKind: 'finance.expense-payment.recorded.v1', sourceVersion: '3',
    currencyId: 'a0000000-0000-4000-8000-000000000004', amount: '100.01', ownerProfileId: 'SENTETIK-profile', ownerProfileVersion: '1',
    occurredOn: '2026-11-17', recordedAtUtc: '2026-11-17T09:00:00Z', receivedAtUtc: '2026-11-17T09:01:00Z',
    status: 'Received', mappingStatus: 'BindingUnavailable', preparationVersion: null as string | null }] }
}
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })
describe('immutable received Finance source list', () => {
  it('keeps missing binding distinct from ready and neither is a posted journal', () => {
    const body = financeSourcesBody()
    expect(decodeFinanceJournalSources(body, company)).toEqual(body)
    body.items[0].mappingStatus = 'BindingAvailable'; body.items[0].preparationVersion = 'A'.repeat(64)
    expect(decodeFinanceJournalSources(body, company)?.items[0].status).toBe('Received')
  })
  it.each(['foreign', 'unknown', 'duplicate', 'shape', 'amount', 'binding', 'status'])('refuses %s evidence', change => {
    const body = financeSourcesBody()
    if (change === 'foreign') body.items[0].companyId = 'a0000000-0000-4000-8000-000000000099'
    if (change === 'unknown') body.items[0].sourceKind = 'finance.fx.invented.v1'
    if (change === 'duplicate') body.items.push({ ...body.items[0] })
    if (change === 'shape') Object.assign(body.items[0], { invented: true })
    if (change === 'amount') body.items[0].amount = '1e2'
    if (change === 'binding') body.items[0].mappingStatus = 'BindingAvailable'
    if (change === 'status') body.items[0].status = 'Posted'
    expect(decodeFinanceJournalSources(body, company)).toBeNull()
  })
  it.each([401, 403, 404, 409, 503])('preserves HTTP %s instead of an empty success', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status })))
    expect(await fetchFinanceJournalSources(company)).toMatchObject({ type: 'http', status })
  })
  it('keeps empty, malformed and offline distinct', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ companyId: company, items: [] })))
      .mockResolvedValueOnce(new Response('{}')).mockRejectedValueOnce(new Error('offline'))
    vi.stubGlobal('fetch', fetcher)
    expect(await fetchFinanceJournalSources(company)).toMatchObject({ type: 'ok-single', data: { items: [] } })
    expect(await fetchFinanceJournalSources(company)).toEqual({ type: 'malformed' })
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    expect(await fetchFinanceJournalSources(company)).toEqual({ type: 'offline' })
  })
})

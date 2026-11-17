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
  it('retains raw and approved exchange evidence without converting the original leg amounts', () => {
    const original = financeSourcesBody().items[0]
    const transfer = 'a0000000-0000-4000-8000-000000000020'
    const foreign = 'a0000000-0000-4000-8000-000000000021'
    const raw = { ...original, sourceId: transfer, sourceKind: 'finance.cash-exchange.recorded.v1', sourceVersion: '1',
      cashExchange: { transferId: transfer, bookValueId: null, sourceCurrencyId: original.currencyId,
        sourceAmount: original.amount, targetCurrencyId: foreign, targetAmount: '3300', sourceBookValueTl: null, postingState: 'SourceBookValuePending' } }
    expect(decodeFinanceJournalSources({ companyId: company, items: [raw] }, company)?.items[0]).toEqual(raw)
    const approved = { ...original, sourceKind: 'finance.cash-exchange.book-value-approved.v1', sourceVersion: '2', amount: '3100',
      mappingStatus: 'BindingAvailable', preparationVersion: 'A'.repeat(64),
      cashExchange: { transferId: transfer, bookValueId: original.sourceId, sourceCurrencyId: foreign,
        sourceAmount: '100', targetCurrencyId: original.currencyId, targetAmount: '3300', sourceBookValueTl: '3100', postingState: 'ApprovedSourceBookValue' } }
    expect(decodeFinanceJournalSources({ companyId: company, items: [approved] }, company)?.items[0]).toEqual(approved)
    for (const changed of [
      { ...raw, mappingStatus: 'BindingAvailable', preparationVersion: 'A'.repeat(64) },
      { ...approved, cashExchange: { ...approved.cashExchange, bookValueId: transfer } },
      { ...approved, cashExchange: { ...approved.cashExchange, sourceBookValueTl: '3200' } },
      { ...approved, cashExchange: { ...approved.cashExchange, targetCurrencyId: foreign } },
      { ...approved, cashExchange: { ...approved.cashExchange, postingState: 'TargetBookValueUnavailable' } },
      { ...approved, cashExchange: null },
    ]) expect(decodeFinanceJournalSources({ companyId: company, items: [changed] }, company)).toBeNull()
    const targetUnknown = { ...approved, mappingStatus: 'BindingUnavailable', preparationVersion: null,
      cashExchange: { ...approved.cashExchange, targetCurrencyId: 'a0000000-0000-4000-8000-000000000022', postingState: 'TargetBookValueUnavailable' } }
    expect(decodeFinanceJournalSources({ companyId: company, items: [targetUnknown] }, company)?.items[0]).toEqual(targetUnknown)
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

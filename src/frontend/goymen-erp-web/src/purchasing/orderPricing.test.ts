import { afterEach, describe, expect, it, vi } from 'vitest'
import { createPurchaseOrder, fetchPurchasingCurrencyCandidates, PURCHASING_CURRENCY_CANDIDATES_PATH } from './api'
import { decodePurchaseOrderCommandResponse, decodePurchaseOrderDetail, decodePurchasingCurrencyCandidates, type CreatePurchaseOrderRequest } from './contract'

/**
 * A2a-1 (TEK-01, API-20; ALS-D5, ALS-D18, PRS-02): the priced order body decodes only when its disclosure and its prices agree,
 * the currency candidates decode exactly, and a priced create sends exactly what was typed and reads the price refusal back.
 * SENTETIK.
 */
const COMPANY = '10000000-0000-0000-0000-000000000001'
const CURRENCY = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const UNIT = '77777777-7777-4777-8777-777777777777'
const summary = {
  id: '11111111-1111-4111-8111-111111111111', companyId: COMPANY, status: 'Draft', version: '2',
  supplierPartyId: '33333333-3333-4333-8333-333333333333', companyAccountId: '44444444-4444-4444-8444-444444444444', sourceRequestId: null,
  effectiveOn: '2026-11-07', createdAtUtc: '2026-11-07T10:00:00Z', updatedAtUtc: '2026-11-07T10:00:00Z', lineCount: 1,
}
const readSummary = { ...summary, supplierDisplay: { partyId: summary.supplierPartyId, code: 'CAR-1', legalName: 'SENTETIK Tedarikçi', tradeName: null } }
const price = { unitPrice: '41.25', priceUnitId: UNIT, discountPercent: '2.5', discountAmount: null, netUnitPrice: '40.21875', lineAmount: '50293.55' }
const line = (extra: Record<string, unknown> = {}) => ({
  lineId: '55555555-5555-4555-8555-555555555555', lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null,
  productId: '66666666-6666-4666-8666-666666666666', productVersion: '7', unitId: UNIT, ordered: '1250.5', technical: '0',
  committed: '1250.5', accepted: '0', remaining: '1250.5', cancelled: '0', price, ...extra,
})
const currency = { currencyId: CURRENCY, currencyCode: 'TRY', scale: 2 }
const body = (extra: Record<string, unknown> = {}, lines: unknown[] = [line()]) => ({
  order: readSummary, lines, acceptances: [], cancellation: null, priceDisclosure: 'Visible', currency, pricePending: false, ...extra,
})

afterEach(() => vi.unstubAllGlobals())

describe('A2a-1 priced order body', () => {
  it('decodes a visible price exactly as the server sent it', () => {
    const detail = decodePurchaseOrderDetail(body())
    expect(detail?.priceDisclosure).toBe('Visible')
    expect(detail?.currency).toEqual(currency)
    expect(detail?.lines[0].price).toEqual(price)
    expect(decodePurchaseOrderCommandResponse({ detail: { ...body(), order: summary }, isReplay: false, originalCorrelationId: 'c' })?.detail.lines[0].price)
      .toEqual(price)
  })

  it('decodes "fiyat bekliyor" and a withheld body', () => {
    expect(decodePurchaseOrderDetail(body({ pricePending: true, currency: null }, [line({ price: null })]))?.pricePending).toBe(true)
    const withheld = decodePurchaseOrderDetail(body({ priceDisclosure: 'Withheld', pricePending: null }, [line({ price: null })]))
    expect([withheld?.priceDisclosure, withheld?.pricePending, withheld?.lines[0].price]).toEqual(['Withheld', null, null])
  })

  it.each([
    ['a withheld body that still carries a price', body({ priceDisclosure: 'Withheld', pricePending: null })],
    ['a withheld body with a pending flag', body({ priceDisclosure: 'Withheld', pricePending: false }, [line({ price: null })])],
    ['a visible body whose pending flag lies', body({ pricePending: true })],
    ['a visible body without a pending flag', body({ pricePending: null })],
    ['a priced line without a currency', body({ currency: null })],
    ['an unknown disclosure', body({ priceDisclosure: 'Partial' })],
    ['a line without its price member', body({}, [line({ price: undefined })])],
    ['a priced technical line', body({}, [line({ kind: 'Technical', parentLineId: '99999999-9999-4999-8999-999999999999' })])],
    ['a zero unit price', body({}, [line({ price: { ...price, unitPrice: '0' } })])],
    ['two discounts', body({}, [line({ price: { ...price, discountAmount: '1' } })])],
    ['a numeric amount', body({}, [line({ price: { ...price, lineAmount: 50293.55 } })])],
    ['an extra price field', body({}, [line({ price: { ...price, vatRate: '20' } })])],
    ['a currency scale out of range', body({ currency: { ...currency, scale: 29 } })],
    ['a blank currency code', body({ currency: { ...currency, currencyCode: ' ' } })],
    ['a price on a body from before A2a-1', { order: readSummary, lines: [line()], acceptances: [], cancellation: null }],
  ])('refuses %s', (_, value) => {
    expect(decodePurchaseOrderDetail(value)).toBeNull()
  })
})

describe('A2a-1 currency candidates and the priced create', () => {
  it('decodes the candidates exactly and refuses a duplicate or an extra field', () => {
    expect(decodePurchasingCurrencyCandidates({ items: [{ currencyId: CURRENCY, code: 'TRY', displayLabel: 'TRY' }] })?.items[0].code).toBe('TRY')
    expect(decodePurchasingCurrencyCandidates({ items: [] })?.items).toEqual([])
    expect(decodePurchasingCurrencyCandidates({ items: [{ currencyId: CURRENCY, code: 'TRY', displayLabel: 'TRY' }, { currencyId: CURRENCY, code: 'TRY', displayLabel: 'TL' }] })).toBeNull()
    expect(decodePurchasingCurrencyCandidates({ items: [{ currencyId: CURRENCY, code: 'TRY', displayLabel: 'TRY', scale: 2 }] })).toBeNull()
  })

  it('reads the candidates from the purchasing references and keeps an unavailable owner an http outcome', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ items: [{ currencyId: CURRENCY, code: 'TRY', displayLabel: 'TRY' }] }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const ok = await fetchPurchasingCurrencyCandidates(COMPANY)
    expect(ok.type).toBe('ok-single')
    expect((fetchMock.mock.calls[0] as unknown[])[0]).toBe(PURCHASING_CURRENCY_CANDIDATES_PATH)
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 503 })))
    expect(await fetchPurchasingCurrencyCandidates(COMPANY)).toMatchObject({ type: 'http', status: 503 })
  })

  it('sends the price and the currency as typed and reads a price refusal from the problem errors', async () => {
    const request: CreatePurchaseOrderRequest = {
      supplierPartyId: summary.supplierPartyId, companyAccountId: summary.companyAccountId, sourceRequestId: null, effectiveOn: summary.effectiveOn,
      lines: [{ lineId: line().lineId, productId: line().productId, expectedProductVersion: '7', unitId: UNIT, quantity: '1250.5', sourceRequestLineId: null,
        price: { unitPrice: '41.25', discountPercent: '2.5' } }],
      currencyId: CURRENCY,
    }
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ code: 'validation_failed', errors: [{ field: 'lines[0].price', code: 'purchasing_price_scale_exceeded' }] }),
      { status: 422 }))
    vi.stubGlobal('fetch', fetchMock)
    const outcome = await createPurchaseOrder(request, { companyId: COMPANY, idempotencyKey: '12121212-1212-4212-8212-121212121212' })
    expect(outcome).toMatchObject({ type: 'failed', status: 422, priceCode: 'purchasing_price_scale_exceeded' })
    const sent = JSON.parse(((fetchMock.mock.calls[0] as unknown[])[1] as RequestInit).body as string)
    expect(sent.currencyId).toBe(CURRENCY)
    expect(sent.lines[0].price).toEqual({ unitPrice: '41.25', discountPercent: '2.5' })
  })
})

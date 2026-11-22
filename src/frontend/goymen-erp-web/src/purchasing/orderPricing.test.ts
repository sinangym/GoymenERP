import { afterEach, describe, expect, it, vi } from 'vitest'
import { createPurchaseOrder, fetchPurchasingCurrencyCandidates, PURCHASING_CURRENCY_CANDIDATES_PATH } from './api'
import { decodePurchaseOrderCommandResponse, decodePurchaseOrderDetail, decodePurchaseOrderList, decodePurchasingCurrencyCandidates, type CreatePurchaseOrderRequest } from './contract'

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

  it('decodes an explicit 0 price as a price, not as "fiyat bekliyor"', () => {
    const zero = { ...price, unitPrice: '0', discountPercent: null, netUnitPrice: '0', lineAmount: '0' }
    expect(decodePurchaseOrderDetail(body({}, [line({ price: zero })]))?.lines[0].price?.lineAmount).toBe('0')
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
    ['a negative unit price', body({}, [line({ price: { ...price, unitPrice: '-1' } })])],
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

describe('A2a-2 maturity surcharge and the list flag', () => {
  it('decodes a price with a surcharge, and one sent before A2a-2 without the members', () => {
    const surcharged = { ...price, deferralPercent: '3', deferralAmount: null, netUnitPrice: '41.4253125', lineAmount: '51802.36' }
    expect(decodePurchaseOrderDetail(body({}, [line({ price: surcharged })]))?.lines[0].price?.deferralPercent).toBe('3')
    expect(decodePurchaseOrderDetail(body({}, [line({ price: { ...price, deferralPercent: null, deferralAmount: null } })]))?.lines[0].price)
      .toEqual({ ...price, deferralPercent: null, deferralAmount: null })
  })

  it.each([
    ['two surcharges', { ...price, deferralPercent: '1', deferralAmount: '1' }],
    ['a numeric surcharge', { ...price, deferralPercent: 3, deferralAmount: null }],
    ['only one of the two surcharge members', { ...price, deferralPercent: '3' }],
  ])('refuses %s', (_, value) => {
    expect(decodePurchaseOrderDetail(body({}, [line({ price: value })]))).toBeNull()
  })

  it('decodes the list item flag only as a boolean', () => {
    const page = (item: unknown) => decodePurchaseOrderList({ items: [item], nextCursor: null })
    expect(page({ ...readSummary, pricePending: true })?.items[0].pricePending).toBe(true)
    expect(page(readSummary)?.items[0].pricePending).toBeUndefined()
    expect(page({ ...readSummary, pricePending: null })).toBeNull()
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

describe('AK-5 kg price indicator (SAT-D436, SAT-D439)', () => {
  // Golden K10: 200 pieces × 45,00 → theoretical 9 000,00 / 500,000 kg = 18,00; the real price is not wired yet.
  const kgPrice = { theoreticalKgPrice: '18.00', theoreticalUnavailable: null, realKgPrice: null, realUnavailable: 'RealizationNotAvailable' }

  it('decodes the line and the document kg price exactly as the server sent them', () => {
    const detail = decodePurchaseOrderDetail(body({ kgPrice }, [line({ kgPrice })]))
    expect(detail?.kgPrice).toEqual(kgPrice)
    expect(detail?.lines[0].kgPrice).toEqual(kgPrice)
    expect(decodePurchaseOrderDetail(body())?.kgPrice).toBeUndefined()
  })

  it('decodes a document level hidden by the N2 interim rule as a reason, never a value', () => {
    const hidden = { theoreticalKgPrice: null, theoreticalUnavailable: 'NotAllLinesHaveKg', realKgPrice: null, realUnavailable: 'RealizationNotAvailable' }
    expect(decodePurchaseOrderDetail(body({ kgPrice: hidden }))?.kgPrice?.theoreticalUnavailable).toBe('NotAllLinesHaveKg')
  })

  it.each([
    ['a price and a reason together', { ...kgPrice, theoreticalUnavailable: 'ZeroKg' }],
    ['neither a price nor a reason', { ...kgPrice, theoreticalKgPrice: null }],
    ['a price with three decimals', { ...kgPrice, theoreticalKgPrice: '18.004' }],
    ['a price without decimals', { ...kgPrice, theoreticalKgPrice: '18' }],
    ['an unknown reason', { ...kgPrice, realUnavailable: 'Guessed' }],
    ['an extra field', { ...kgPrice, cost: '1.00' }],
  ])('fails closed on %s', (_name, broken) => {
    expect(decodePurchaseOrderDetail(body({ kgPrice: broken }))).toBeNull()
    expect(decodePurchaseOrderDetail(body({}, [line({ kgPrice: broken })]))).toBeNull()
  })

  it('fails closed on a kg price beside withheld prices (PRS-02)', () => {
    const withheld = { priceDisclosure: 'Withheld', pricePending: null }
    expect(decodePurchaseOrderDetail(body({ ...withheld, kgPrice }, [line({ price: null })]))).toBeNull()
    expect(decodePurchaseOrderDetail(body(withheld, [line({ price: null, kgPrice })]))).toBeNull()
  })
})

describe('AK-3 approval refusal text', () => {
  it('names the missing theoretical kg per piece in user words', async () => {
    const { ORDER_APPROVAL_PROBLEM_TEXT } = await import('./purchaseOrderDetailText')
    expect(ORDER_APPROVAL_PROBLEM_TEXT.purchasing_theoretical_kilograms_missing).toContain('teorik kg/adet')
  })
})

describe('AK-3b order realization (SAT-D436 TK6, TK8)', () => {
  // Golden K02: 40 pieces invoiced for 10 000,00; 500,16 kg weighed against 493,8 kg theoretical → +6,36 kg, +1,29 %, 19,99 /kg.
  const k02 = { invoicedQuantity: '40', invoicedAmount: '10000.00', weighedKg: '500.16', theoreticalKg: '493.8', differenceKg: '6.36',
    deviationPercent: '1.29', realKgPrice: '19.99', realUnavailable: null }
  const k02KgPrice = { theoreticalKgPrice: '20.25', theoreticalUnavailable: null, realKgPrice: '19.99', realUnavailable: null }
  const notInvoiced = { invoicedQuantity: '0', invoicedAmount: '0.00', weighedKg: null, theoreticalKg: null, differenceKg: null,
    deviationPercent: null, realKgPrice: null, realUnavailable: 'NotInvoiced' }
  const documentOf = (realization: Record<string, unknown>) => ({ ...realization, invoicedQuantity: null })

  it('decodes the K02 line and document realization exactly as the server sent them', () => {
    const detail = decodePurchaseOrderDetail(body({ kgPrice: k02KgPrice, realization: documentOf(k02) }, [line({ kgPrice: k02KgPrice, realization: k02 })]))
    expect(detail?.lines[0].realization).toEqual(k02)
    expect(detail?.lines[0].kgPrice).toEqual(k02KgPrice)
    expect(detail?.realization).toEqual(documentOf(k02))
  })

  it('decodes "nothing invoiced" as a true zero with no kg figure, and NotInvoiced as a kg price reason', () => {
    const kgPrice = { ...k02KgPrice, realKgPrice: null, realUnavailable: 'NotInvoiced' }
    const detail = decodePurchaseOrderDetail(body({ kgPrice, realization: documentOf(notInvoiced) }, [line({ kgPrice, realization: notInvoiced })]))
    expect(detail?.lines[0].realization).toEqual(notInvoiced)
    expect(detail?.realization?.realUnavailable).toBe('NotInvoiced')
    expect(detail?.kgPrice?.realUnavailable).toBe('NotInvoiced')
  })

  it('decodes a short delivery (signed difference and deviation) and every unknown figure as null', () => {
    const short = { ...k02, weighedKg: '469.11', differenceKg: '-24.69', deviationPercent: '-5.00', realKgPrice: '21.32' }
    expect(decodePurchaseOrderDetail(body({}, [line({ realization: short })]))?.lines[0].realization).toEqual(short)
    const unweighed = { ...k02, weighedKg: null, differenceKg: null, deviationPercent: null, realKgPrice: null, realUnavailable: 'WeighedKgUnavailable' }
    expect(decodePurchaseOrderDetail(body({}, [line({ realization: unweighed })]))?.lines[0].realization).toEqual(unweighed)
    const hidden = { ...documentOf(k02), invoicedAmount: null, realKgPrice: null, realUnavailable: 'NotAllLinesHaveKg' }
    expect(decodePurchaseOrderDetail(body({ realization: hidden }))?.realization).toEqual(hidden)
  })

  it('an absent realization stays absent (command responses, older bodies)', () => {
    const detail = decodePurchaseOrderDetail(body())
    expect(detail?.realization).toBeUndefined()
    expect(detail?.lines[0].realization).toBeUndefined()
  })

  it.each([
    ['an unknown reason code', { ...notInvoiced, realUnavailable: 'Guessed' }],
    ['a price and a reason together', { ...k02, realUnavailable: 'ZeroKg' }],
    ['neither a price nor a reason', { ...k02, realKgPrice: null }],
    ['a missing member', (({ deviationPercent: _dropped, ...rest }) => rest)(k02)],
    ['an extra member', { ...k02, cost: '1.00' }],
    ['a numeric amount', { ...k02, invoicedAmount: 10000 }],
    ['a numeric kg', { ...k02, weighedKg: 500.16 }],
    ['a non-canonical kg', { ...k02, theoreticalKg: '493.80' }],
    ['a negative zero difference', { ...k02, differenceKg: '-0' }],
    ['a deviation without two decimals', { ...k02, deviationPercent: '1.3' }],
    ['a real kg price with three decimals', { ...k02, realKgPrice: '19.990' }],
    ['an undefined figure', { ...k02, weighedKg: undefined }],
    ['null instead of an object', null],
  ])('refuses %s on a line and on the document', (_, broken) => {
    expect(decodePurchaseOrderDetail(body({}, [line({ realization: broken })]))).toBeNull()
    expect(decodePurchaseOrderDetail(body({ realization: broken === null ? null : { ...(broken as object), invoicedQuantity: null } }))).toBeNull()
  })

  it('fails closed on a document realization with an invoiced quantity, on a technical slice and beside withheld or absent prices', () => {
    expect(decodePurchaseOrderDetail(body({ realization: k02 }))).toBeNull()
    const technical = line({ lineId: '99999999-9999-4999-8999-999999999999', lineNo: 2, kind: 'Technical',
      parentLineId: '55555555-5555-4555-8555-555555555555', price: null, realization: k02 })
    expect(decodePurchaseOrderDetail(body({ order: { ...readSummary, lineCount: 2 } }, [line(), technical]))).toBeNull()
    const withheld = { priceDisclosure: 'Withheld', pricePending: null }
    expect(decodePurchaseOrderDetail(body({ ...withheld, realization: documentOf(k02) }, [line({ price: null })]))).toBeNull()
    expect(decodePurchaseOrderDetail(body(withheld, [line({ price: null, realization: k02 })]))).toBeNull()
    expect(decodePurchaseOrderDetail({ order: readSummary, lines: [line({ price: undefined, realization: k02 })], acceptances: [], cancellation: null })).toBeNull()
  })
})

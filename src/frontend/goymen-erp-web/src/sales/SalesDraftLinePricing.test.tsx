/**
 * AT-018 / AT-021 on S-04 in the real shell: the person fetches the line's list prices, uses one (it becomes the line's
 * reference AND its default price), adds a discount and a maturity surcharge, saves once; the re-read shows the reference,
 * the adjustments and the SERVER's net (the screen computes nothing); an overwritten price reads as manual with the
 * reference still visible; a below-floor refusal is explained on its line. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_PRICE_WRITE_CAPABILITY } from './capabilities'
import { LINE_PRICING_TEXT as P, ORDER_DETAIL_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, KG, installFetch, json, renderSalesApp, type RecordedCall } from './testHarness'

type Body = Record<string, unknown> & { draft: Record<string, unknown>; lines: Record<string, unknown>[] }

const LINE = '66666666-6666-6666-6666-666666666666'
const LIST = 'abababab-abab-4bab-8bab-abababababab'

function detail(version: string, line: Record<string, unknown> = {}): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, version, companyId: COMPANY_A }
  body.lines = [{ ...body.lines[0], ...line }]
  return body
}

const reference = {
  priceListId: LIST, priceListName: 'Yurt içi satış', versionNo: 2, validFrom: '2026-09-01', validThrough: null, currencyCode: 'TRY',
  priceUnitId: KG, unitPrice: '42000.50', floorUnitPrice: '40000', vatBasis: 'ExcludingVat',
}

function pricing(version: string, over: Record<string, unknown> = {}) {
  return {
    version,
    lines: [{ lineId: LINE, lineNo: 1, unitPrice: null, priceUnitId: null, priceSource: null, priceReference: null, discountPercent: null,
      discountAmount: null, surchargePercent: null, surchargeAmount: null, netUnitPrice: null, ...over }],
  }
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const path = '/satis/siparisler/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'

describe('AT-018 list price on the order line', () => {
  it('uses the list price as reference and default, adds a discount and a surcharge, and reads back the server net', async () => {
    let saved = false
    const posts: RecordedCall[] = []
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_WRITE_CAPABILITY],
      onDetail: () => json(saved ? detail('3', { unitPrice: '42000.50', priceUnitId: KG, discountPercent: '3' }) : detail('2')),
      onLinePricing: () => json(saved
        ? pricing('3', { unitPrice: '42000.50', priceUnitId: KG, priceSource: 'Reference', priceReference: reference, discountPercent: '3',
          surchargePercent: '1.5', netUnitPrice: '41351.592275' })
        : pricing('2')),
      onPriceReferences: () => json({ lineId: LINE, currencyCode: 'TRY', on: '2026-09-13', items: [reference] }),
      onCommercialRevision: (call) => {
        posts.push(call)
        saved = true
        return json({ detail: detail('3', { unitPrice: '42000.50', priceUnitId: KG, discountPercent: '3' }), isReplay: false,
          originalCorrelationId: 'c1' })
      },
    })
    renderSalesApp(path)

    const form = await screen.findByTestId('sales-price-revision-form')
    expect(within(form).getByTestId('sales-price-reference-1').textContent).toBe(`${P.reference}: ${P.noReference}`)
    await userEvent.click(within(form).getByTestId('sales-price-fetch-1'))
    await userEvent.click(await within(form).findByRole('button', { name: P.useReference }))
    // The chosen list price is the reference AND the default price (the person may still type another).
    expect((within(form).getByLabelText(ORDER_DETAIL_TEXT.priceRevision.unitPrice) as HTMLInputElement).value).toBe('42000.50')
    expect(within(form).getByTestId('sales-price-reference-1').textContent).toContain('Yurt içi satış · Sürüm 2: 42000.50 TRY')
    await userEvent.type(within(form).getByLabelText(ORDER_DETAIL_TEXT.priceRevision.discount), '3')
    await userEvent.type(within(form).getByLabelText(P.surchargeValue), '1.5')
    await userEvent.click(within(form).getByRole('button', { name: ORDER_DETAIL_TEXT.priceRevision.submit }))

    await waitFor(() => expect(screen.getByTestId('sales-line-1-net').textContent).toBe('41351.592275'))
    const sent = JSON.parse(posts[0].body ?? '{}') as { expectedVersion: string; lines: Record<string, unknown>[] }
    expect(sent.expectedVersion).toBe('2')
    expect(sent.lines[0]).toEqual({
      lineId: LINE, unitPrice: '42000.50', priceUnitId: KG, discountPercent: '3',
      priceReference: { priceListId: LIST, versionNo: 2 }, discountAmount: null, surchargePercent: '1.5', surchargeAmount: null,
    })
    // Reopened from the server: the reference, the source, the adjustments and the net are all visible.
    expect(screen.getByTestId('sales-line-1-reference').textContent).toContain('Yurt içi satış · Sürüm 2: 42000.50 TRY')
    expect(screen.getByTestId('sales-line-1-source').textContent).toBe(P.sourceReference)
    expect(screen.getByTestId('sales-line-1-discount-adjustment').textContent).toBe('%3')
    expect(screen.getByTestId('sales-line-1-surcharge').textContent).toBe('%1.5')
  })

  it('an overwritten price with amounts reads back as manual with the reference still visible', async () => {
    installFetch({
      capabilities: [...ALL_SALES],
      onDetail: () => json(detail('4', { unitPrice: '41000', priceUnitId: KG })),
      onLinePricing: () => json(pricing('4', { unitPrice: '41000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference,
        discountAmount: '500', surchargeAmount: '250', netUnitPrice: '40750' })),
    })
    renderSalesApp(path)
    expect((await screen.findByTestId('sales-line-1-source')).textContent).toBe(P.sourceManual)
    expect(screen.getByTestId('sales-line-1-reference').textContent).toContain('42000.50')
    expect(screen.getByTestId('sales-line-1-discount-adjustment').textContent).toBe(P.amount('500'))
    expect(screen.getByTestId('sales-line-1-net').textContent).toBe('40750')
    expect(screen.queryByTestId('sales-price-revision-form')).toBeNull()
  })

  it('a net below the list floor is refused and explained on its line', async () => {
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_WRITE_CAPABILITY],
      onDetail: () => json(detail('2')),
      onLinePricing: () => json(pricing('2')),
      onPriceReferences: () => json({ lineId: LINE, currencyCode: 'TRY', on: '2026-09-13', items: [reference] }),
      onCommercialRevision: () => json({ status: 422, title: 'x', code: 'validation_failed', correlationId: 'c',
        errors: [{ field: 'lines[0].unitPrice', code: 'sales_price_below_floor' }] }, 422),
    })
    renderSalesApp(path)
    const form = await screen.findByTestId('sales-price-revision-form')
    await userEvent.click(within(form).getByTestId('sales-price-fetch-1'))
    await userEvent.click(await within(form).findByRole('button', { name: P.useReference }))
    await userEvent.type(within(form).getByLabelText(ORDER_DETAIL_TEXT.priceRevision.discount), '10')
    await userEvent.click(within(form).getByRole('button', { name: ORDER_DETAIL_TEXT.priceRevision.submit }))
    expect((await screen.findByTestId('sales-price-line-error')).textContent)
      .toBe(P.lineError(1, P.fieldError.sales_price_below_floor))
  })
})

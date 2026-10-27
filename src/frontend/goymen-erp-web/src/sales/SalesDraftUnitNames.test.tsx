/**
 * TEK-11 on S-04: the order detail names what the order already names. A line's order unit and its price unit read as the
 * unit code the order carries; the company account and currency ids are folded under "Teknik kimlikler" (the currency is
 * named in the delivery block). A unit the order does not name stays its stored id, never a guessed code. Component
 * evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { decodeDraftDetail } from './contract'
import { ORDER_DETAIL_TEXT as T } from './salesText'
import { orderUnitCodes } from './unitNames'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const KG = '44444444-4444-4444-4444-444444444444'
const TON = '77777777-7777-4777-8777-777777777777'
const ACCOUNT = '55555555-5555-5555-5555-555555555555'
const CURRENCY = '33333333-3333-3333-3333-333333333333'

type Body = Record<string, any>

function detail(line: Record<string, unknown>): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A }
  body.lines = [{ ...body.lines[0], ...line }]
  return body
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('TEK-11 names instead of stored ids', () => {
  it('shows the order and price unit by the code the order carries', async () => {
    installFetch({
      capabilities: [...ALL_SALES],
      onDetail: () => json(detail({ orderUnitCode: 'KG', unitPrice: '42.50', priceUnitId: KG })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    expect((await screen.findByTestId('sales-line-1-order-unit')).textContent).toBe('KG')
    expect(screen.getByTestId('sales-line-1-price').textContent).toBe('42.50 / KG')
    expect(screen.getByTestId('sales-line-1').textContent).not.toContain(KG)
  })

  it('keeps a unit the order does not name as its stored id', async () => {
    installFetch({
      capabilities: [...ALL_SALES],
      onDetail: () => json(detail({ orderUnitCode: 'KG', unitPrice: '42.50', priceUnitId: TON })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    expect((await screen.findByTestId('sales-line-1-price')).textContent).toBe(`42.50 / ${TON}`)
  })

  it('folds the account and currency ids under the technical ids, out of the main facts', async () => {
    installFetch({ capabilities: [...ALL_SALES], onDetail: () => json(detail({})) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const folded = await screen.findByTestId('sales-draft-technical-ids')
    expect(folded.tagName).toBe('DETAILS')
    expect((folded as HTMLDetailsElement).open).toBe(false)
    expect(within(folded).getByText(T.technicalIds)).toBeTruthy()
    expect(within(folded).getByText(ACCOUNT)).toBeTruthy()
    expect(within(folded).getByText(CURRENCY)).toBeTruthy()
    expect(screen.getByTestId('sales-order-status-facts').textContent).not.toContain(ACCOUNT)
  })
})

describe('orderUnitCodes', () => {
  it('collects the line codes and the summary snapshots, and nothing else', () => {
    const body = detail({ orderUnitCode: 'KG' })
    body.draft.productSnapshots = [{
      productId: '22222222-2222-2222-2222-222222222222', productVersion: '7', productCode: 'EXAMPLE-ONLY',
      productName: 'Controlled schema example', orderUnitId: TON, orderUnitCode: 'TON',
    }]
    const decoded = decodeDraftDetail(body, DRAFT)
    expect(decoded).not.toBeNull()
    expect([...orderUnitCodes(decoded!)].sort()).toEqual([[KG, 'KG'], [TON, 'TON']].sort())
  })

  it('names nothing when the order carries no code', () => {
    const decoded = decodeDraftDetail(detail({}), DRAFT)
    expect(decoded).not.toBeNull()
    expect(orderUnitCodes(decoded!).size).toBe(0)
  })
})

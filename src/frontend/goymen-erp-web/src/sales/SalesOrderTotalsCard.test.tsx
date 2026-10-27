/**
 * API-03 / UI-15 on S-04: the "Sipariş toplamları" card shows the server calculator's amounts verbatim — per line and for the
 * document — and never an amount the server did not compute: an unpriced line, an incomplete document and a tax without a
 * source (no VAT master) are words with their reason, never zero. A caller whose prices are withheld is not asked. Loading,
 * 503 (with its support code), 404, 401/403, malformed and foreign answers show no amount. Component evidence, NOT_ENV: the
 * API answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import totalsResponseRaw from './examples/totals-response.json?raw'
import { SALES_CAPABILITY } from './capabilities'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { decodeOrderTotals } from './orderTotals'
import { ORDER_TOTALS_TEXT as T } from './salesText'
import { OrderTotalsCard } from './SalesOrderTotalsCard'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE_1 = 'aaaaaaaa-1111-4111-8111-000000000001'
const LINE_2 = 'aaaaaaaa-1111-4111-8111-000000000002'
const UNIT = 'bbbbbbbb-1111-4111-8111-000000000002'
const CURRENCY = 'cccccccc-1111-4111-8111-000000000003'
const PATH = `/satis/siparisler/${DRAFT}`

type Body = Record<string, any>

/** The pinned detail at version 4 in company A: line 1 (1.25, priced at 12.50 − 10 %), line 2 (2, unpriced). */
function detail(disclosure: 'Visible' | 'Withheld' = 'Visible'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A, version: '4', currencyId: CURRENCY, lineCount: 2 }
  const base = { ...body.lines[0], orderUnitId: UNIT, orderUnitCode: 'KG', requestedDeliverySlices: [], requestedDeliveryDate: null }
  const priced = disclosure === 'Visible'
  body.lines = [
    { ...base, lineId: LINE_1, lineNo: 1, quantity: '1.25', unitPrice: priced ? '12.50' : null, priceUnitId: priced ? UNIT : null,
      discountPercent: priced ? '10' : null, priceDisclosure: disclosure },
    { ...base, lineId: LINE_2, lineNo: 2, productCode: 'URUN-2', productName: 'İkinci ürün', quantity: '2', priceDisclosure: disclosure },
  ]
  return body
}

/** The pinned example: line 1 computed (14.34), line 2 unpriced, the document incomplete, no VAT master. */
const totals = (): Body => JSON.parse(totalsResponseRaw) as Body

/** Both lines priced: the document net is the server's sum; the tax still has no source. */
function complete(): Body {
  const body = totals()
  body.lines[1] = { ...body.lines[0], lineId: LINE_2, lineNo: 2, quantity: '2', netUnitPrice: '100', netAmount: '200.00' }
  body.totals = { ...body.totals, state: 'Computed', unpricedLineCount: 0, netAmount: '214.34', taxState: 'NoSource' }
  return body
}

afterEach(() => {
  cleanup()
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const totalsCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/totals'))
const card = () => screen.findByRole('region', { name: T.heading })
/** The card once its line table is on screen (the read answered and decoded). */
async function loaded() {
  const section = await card()
  await within(section).findByRole('table', { name: T.linesCaption })
  return section
}

describe('S-04 order totals (API-03 / UI-15)', () => {
  it('shows the server’s line amounts and says why the document has no total, through one company-bound read-only GET', async () => {
    const harness = installFetch({ onDetail: () => json(detail()), onTotals: () => json(totals()) })
    renderSalesApp(PATH)

    const section = await loaded()
    const table = within(section).getByRole('table', { name: T.linesCaption })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => row.getAttribute('data-state'))).toEqual(['Computed', 'Unpriced'])
    expect(within(section).getByTestId('sales-totals-line-1-net-unit-price').textContent).toBe('11.475')
    expect(within(section).getByTestId('sales-totals-line-1-net').textContent).toBe('14.34')
    expect(within(section).getByTestId('sales-totals-line-1-quantity').textContent).toBe('1.25 KG')
    // An unpriced line has no amount and an unknown tax — the word, never zero.
    for (const cell of ['net-unit-price', 'net', 'vat-percent', 'vat', 'withheld', 'payable']) {
      expect(within(section).getByTestId(`sales-totals-line-2-${cell}`).textContent).toBe(T.notAvailable)
    }
    expect(within(section).getByTestId('sales-totals-line-2-state').textContent)
      .toBe(`${T.lineState.Unpriced} ${T.vat}: ${T.taxState.NotComputed}`)
    // The incomplete document shows no partial sum; the tax has no source.
    for (const id of ['net', 'vat', 'withheld', 'gross', 'payable']) {
      const amount = within(section).getByTestId(`sales-totals-${id}`)
      expect([amount.textContent, amount.getAttribute('data-computed')]).toEqual([T.notAvailable, 'false'])
    }
    expect(within(section).getByTestId('sales-totals-note-incomplete').textContent).toBe(T.incomplete(1, 0))
    expect(within(section).getByTestId('sales-totals-note-tax-no-source').textContent).toBe(T.taxNoSource)
    expect(within(section).getByTestId('sales-totals-revision').textContent).toBe(T.revision('Rev.0', '4'))

    const reads = totalsCalls(harness.calls)
    expect(reads).toHaveLength(1)
    expect(reads[0]).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
    expect(reads[0].url).toContain(`/sales/drafts/${DRAFT}/totals`)
    expect(section.getAttribute('aria-busy')).toBe('false')
  })

  it('shows the document net the server summed when every line is priced, and still no VAT without a VAT master', async () => {
    installFetch({ onDetail: () => json(detail()), onTotals: () => json(complete()) })
    renderSalesApp(PATH)

    const section = await loaded()
    expect(within(section).getByTestId('sales-totals-net').textContent).toBe('214.34')
    expect(within(section).getByTestId('sales-totals-vat').textContent).toBe(T.notAvailable)
    expect(within(section).getByTestId('sales-totals-payable').textContent).toBe(T.notAvailable)
    expect(within(section).queryByTestId('sales-totals-note-incomplete')).toBeNull()
    expect(within(section).getByTestId('sales-totals-note-tax-no-source').textContent).toBe(T.taxNoSource)
    // Without the currencies reference answering, the currency's identity is shown, never a guessed code.
    expect(within(section).getByTestId('sales-totals-currency').textContent).toBe(CURRENCY)
  })

  it('does not ask for totals when the detail withholds prices, and says why nothing is shown', async () => {
    const harness = installFetch({ onDetail: () => json(detail('Withheld')) })
    renderSalesApp(PATH)

    const section = await card()
    expect(within(section).getByTestId('sales-totals-forbidden').textContent).toBe(T.forbidden)
    expect(within(section).queryByRole('table')).toBeNull()
    await waitFor(() => expect(totalsCalls(harness.calls)).toHaveLength(0))
  })

  it('shows a polite loading status while the totals are read, then the answer', async () => {
    let release: (response: Response) => void = () => undefined
    installFetch({ onDetail: () => json(detail()), onTotals: () => new Promise<Response>((resolve) => { release = resolve }) })
    renderSalesApp(PATH)

    const section = await card()
    const loading = await within(section).findByRole('status')
    expect(loading.getAttribute('data-state')).toBe('initial-loading')
    expect(section.getAttribute('aria-busy')).toBe('true')
    expect(within(section).queryByRole('table')).toBeNull()

    release(json(totals()))
    await within(section).findByRole('table', { name: T.linesCaption })
  })

  it('on a 503 shows no amount, names the outage with its support code, and retries the same read-only URL', async () => {
    let failing = true
    const harness = installFetch({
      onDetail: () => json(detail()),
      onTotals: () => (failing
        ? json({ status: 503, title: 'Service Unavailable', code: 'service_unavailable', correlationId: 'corr-503' }, 503)
        : json(totals())),
    })
    renderSalesApp(PATH)

    const section = await card()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('service-unavailable')
    expect(within(section).getByTestId('sales-totals-unreadable').textContent).toBe(T.serviceUnavailable)
    expect(within(section).getByTestId('sales-totals-support-code').textContent).toBe(T.supportCode('corr-503'))
    expect(within(section).queryByRole('table')).toBeNull()

    failing = false
    within(alert).getByRole('button').click()
    await within(section).findByRole('table', { name: T.linesCaption })
    const reads = totalsCalls(harness.calls)
    expect(reads).toHaveLength(2)
    expect(new Set(reads.map((call) => `${call.method} ${call.url}`)).size).toBe(1)
  })

  it('fails closed on 404: no amount, no retry', async () => {
    installFetch({ onDetail: () => json(detail()), onTotals: () => json({ status: 404 }, 404) })
    renderSalesApp(PATH)

    const section = await card()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('not-found')
    expect(within(alert).queryByRole('button')).toBeNull()
    expect(within(section).getByTestId('sales-totals-unreadable').textContent).toBe(T.unreadable)
    expect(within(section).queryByRole('table')).toBeNull()
  })

  it.each([401, 403])('routes a %i through the real auth boundary: the protected detail closes and no amount is left', async (code) => {
    installFetch({ onDetail: () => json(detail()), onTotals: () => json({ status: code }, code) })
    renderSalesApp(PATH)

    expect(await screen.findByText(code === 401 ? 'Oturum sonlandı — oturum' : 'Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByTestId('sales-totals')).toBeNull()
  })

  it('refuses an answer that does not describe exactly this detail and shows it as unexpected, never as amounts', async () => {
    const foreign = totals()
    foreign.draftVersion = '3'
    installFetch({ onDetail: () => json(detail()), onTotals: () => json(foreign) })
    renderSalesApp(PATH)

    const section = await card()
    expect((await within(section).findByRole('alert')).getAttribute('data-state')).toBe('malformed-response')
    expect(within(section).queryByRole('table')).toBeNull()
    expect(within(section).queryByText('14.34')).toBeNull()
  })

  it('is not read without the S-04 detail capability (API-03 publishes none of its own)', async () => {
    const harness = installFetch({
      capabilities: ALL_SALES.filter((id) => id !== SALES_CAPABILITY.draftsDetail),
      onDetail: () => json(detail()),
    })
    renderSalesApp(PATH)
    await screen.findByRole('alert')
    expect(screen.queryByTestId('sales-totals')).toBeNull()
    expect(totalsCalls(harness.calls)).toHaveLength(0)
  })

  it('reads the harness default (the fake server’s fixture) for a detail without an explicit totals answer', async () => {
    installFetch({ onDetail: () => json(detail()) })
    renderSalesApp(PATH)

    const section = await loaded()
    // 1.25 × (12.50 − 10 %) = 14.0625 → 14.06 in the fake server; line 2 unpriced.
    expect(within(section).getByTestId('sales-totals-line-1-net').textContent).toBe('14.06')
    expect(within(section).getByTestId('sales-totals-line-2-net').textContent).toBe(T.notAvailable)
  })
})

describe('OrderTotalsCard outcomes', () => {
  const draftDetail = decodeDraftDetail(detail(), DRAFT) as DraftDetail

  it('shows the forbidden state on its own: totals are prices and the caller may not see them', () => {
    render(<OrderTotalsCard detail={draftDetail} currency="TRY" outcome={{ type: 'http', status: 403 }} onRetry={() => undefined} />)

    const section = screen.getByRole('region', { name: T.heading })
    expect(within(section).getByRole('status').textContent).toBe(T.forbidden)
    expect(within(section).queryByRole('table')).toBeNull()
  })

  it('shows VAT, withholding, gross and payable exactly as the server computed them once a VAT master answers', () => {
    const body = complete()
    body.taxState = 'Computed'
    Object.assign(body.lines[0], { taxState: 'Computed', vatPercent: '20', vatAmount: '2.87', withheldVatAmount: '0.00', grossAmount: '17.21', payableAmount: '17.21' })
    Object.assign(body.lines[1], { taxState: 'Computed', vatPercent: '20', vatAmount: '40.00', withholdingShare: '0.5', withheldVatAmount: '20.00', grossAmount: '240.00', payableAmount: '220.00' })
    Object.assign(body.totals, { taxState: 'Computed', vatAmount: '42.87', withheldVatAmount: '20.00', grossAmount: '257.21', payableAmount: '237.21' })
    const decoded = decodeOrderTotals(body, draftDetail)
    if (decoded === null) throw new Error('the taxed answer does not decode')
    render(<OrderTotalsCard detail={draftDetail} currency="TRY" outcome={{ type: 'ok-single', data: decoded }} onRetry={() => undefined} />)

    expect(['net', 'vat', 'withheld', 'gross', 'payable'].map((id) => screen.getByTestId(`sales-totals-${id}`).textContent))
      .toEqual(['214.34', '42.87', '20.00', '257.21', '237.21'])
    expect(screen.getByTestId('sales-totals-line-2-vat-percent').textContent).toBe(T.percent('20'))
    expect(screen.getByTestId('sales-totals-line-2-withheld').textContent).toBe('20.00')
    expect(screen.getByTestId('sales-totals-line-2-payable').textContent).toBe('220.00')
    expect(screen.getByTestId('sales-totals-currency').textContent).toBe('TRY')
    expect(screen.queryByTestId('sales-totals-note-tax-no-source')).toBeNull()
  })

  it('names a line priced in another unit and the document it leaves incomplete', () => {
    const body = totals()
    Object.assign(body.lines[1], { state: 'PriceUnitNotOrderUnit' })
    Object.assign(body.totals, { unpricedLineCount: 0, priceUnitNotOrderUnitLineCount: 1 })
    const decoded = decodeOrderTotals(body, draftDetail)
    if (decoded === null) throw new Error('the answer does not decode')
    render(<OrderTotalsCard detail={draftDetail} currency="TRY" outcome={{ type: 'ok-single', data: decoded }} onRetry={() => undefined} />)

    expect(screen.getByTestId('sales-totals-line-2-state').textContent).toContain(T.lineState.PriceUnitNotOrderUnit)
    expect(screen.getByTestId('sales-totals-note-incomplete').textContent).toBe(T.incomplete(0, 1))
  })

  it.each([
    [{ type: 'offline' as const }, 'offline'],
    [{ type: 'network-error' as const }, 'network-error'],
    [{ type: 'malformed' as const }, 'malformed-response'],
    [{ type: 'http' as const, status: 500, body: null }, 'service-unavailable'],
  ])('fails closed on %o without any amount', (outcome, kind) => {
    render(<OrderTotalsCard detail={draftDetail} currency="TRY" outcome={outcome} onRetry={() => undefined} />)
    expect(screen.getByRole('alert').getAttribute('data-state')).toBe(kind)
    expect(screen.getByTestId('sales-totals-unreadable').textContent).toBe(T.unreadable)
    expect(screen.queryByRole('table')).toBeNull()
  })
})

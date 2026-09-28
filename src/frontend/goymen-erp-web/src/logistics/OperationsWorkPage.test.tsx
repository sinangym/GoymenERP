/**
 * AT-025 "Operasyon işleri" in the real shell: the sales and purchase sections are separate routes and reads; switching
 * sections never mixes rows; a row opens its owner's screen only with that screen's grant; going back from an owner screen
 * keeps the section and the search, and the reopened list shows the same rows; a section the actor has no grant for is
 * neither offered nor opened; a page of the other section is refused. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LOGISTICS_CAPABILITY } from './capabilities'
import { OPERATIONS_WORK_TEXT } from './logisticsText'
import { FORBIDDEN_MONEY_WORDS, installFetch, json, purchaseRow, renderLogisticsApp, salesRow } from './testHarness'

const SALES_DETAIL = 'sales.drafts.detail'
const PURCHASE_DETAIL = 'purchasing.orders.detail'
const BOTH = [LOGISTICS_CAPABILITY.operationsSales, LOGISTICS_CAPABILITY.operationsPurchase, SALES_DETAIL, PURCHASE_DETAIL]
const SALES_LINE = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
const PURCHASE_LINE = 'ffffffff-ffff-ffff-ffff-ffffffffffff'
const PURCHASE_ORDER = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
const SALES_ORDER = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'

const salesPage = () => json({ section: 'Sales', items: [salesRow()], nextCursor: null })
const purchasePage = () => json({ section: 'Purchase', items: [purchaseRow()], nextCursor: null })

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AT-025 Operasyon işleri', () => {
  it('the sales section shows only sales work, links the Sales order, reads nothing of purchasing and shows no money word', async () => {
    const harness = installFetch({ capabilities: BOTH, onSalesWork: salesPage, onPurchaseWork: purchasePage })
    renderLogisticsApp('/lojistik/operasyon-isleri/satis')
    const row = await screen.findByTestId(`operations-sales-row-${SALES_LINE}`)
    expect(within(row).getByRole('link', { name: 'SENTETIK SO-0001' }).getAttribute('href')).toBe(`/satis/siparisler/${SALES_ORDER}`)
    expect(within(row).getByText('Sentetik Müşteri')).toBeTruthy()
    expect(screen.queryByTestId('operations-purchase-table')).toBeNull()
    expect(screen.getByTestId('operations-tab-sales').getAttribute('aria-current')).toBe('page')
    expect(screen.getByTestId('operations-tab-purchase')).toBeTruthy()
    const calls = harness.logisticsCalls()
    expect(calls.map((call) => call.url)).toEqual(['/api/v1/logistics/operations-work/sales?pageSize=50'])
    expect(calls[0].companyId).not.toBeNull()
    for (const word of FORBIDDEN_MONEY_WORDS) expect(document.body.textContent).not.toContain(word)
  })

  it('switching to the purchase section reads the purchase route only and no sales row appears in it', async () => {
    const harness = installFetch({ capabilities: BOTH, onSalesWork: salesPage, onPurchaseWork: purchasePage })
    renderLogisticsApp('/lojistik/operasyon-isleri/satis')
    await screen.findByTestId(`operations-sales-row-${SALES_LINE}`)
    await userEvent.click(screen.getByTestId('operations-tab-purchase'))
    const table = await screen.findByTestId('operations-purchase-table')
    const row = within(table).getByTestId(`operations-purchase-row-${PURCHASE_LINE}`)
    expect(within(row).getByRole('link').getAttribute('href')).toBe(`/satinalma/siparisler/${PURCHASE_ORDER}`)
    expect(within(row).getByText('60')).toBeTruthy()
    expect(within(table).queryByText('SENTETIK SO-0001')).toBeNull()
    expect(screen.queryByTestId('operations-sales-table')).toBeNull()
    expect(screen.getByTestId('operations-section-intro').textContent).toBe(OPERATIONS_WORK_TEXT.purchaseIntro)
    expect(harness.logisticsCalls().map((call) => call.url)).toEqual([
      '/api/v1/logistics/operations-work/sales?pageSize=50',
      '/api/v1/logistics/operations-work/purchase?pageSize=50',
    ])
  })

  it('back from the owner screen keeps the section and the search, and the reopened list shows the same row', async () => {
    const harness = installFetch({ capabilities: BOTH, onSalesWork: salesPage, onPurchaseWork: purchasePage })
    const { router } = renderLogisticsApp('/lojistik/operasyon-isleri/satinalma')
    await screen.findByTestId(`operations-purchase-row-${PURCHASE_LINE}`)
    await userEvent.type(screen.getByLabelText(OPERATIONS_WORK_TEXT.filter), 'Tedarik')
    await waitFor(() => expect(router.state.location.search).toBe('?ara=Tedarik'))
    await userEvent.click(within(screen.getByTestId(`operations-purchase-row-${PURCHASE_LINE}`)).getByRole('link'))
    await waitFor(() => expect(router.state.location.pathname).toBe(`/satinalma/siparisler/${PURCHASE_ORDER}`))
    await router.navigate(-1)
    await waitFor(() => expect(router.state.location.pathname).toBe('/lojistik/operasyon-isleri/satinalma'))
    expect(router.state.location.search).toBe('?ara=Tedarik')
    await screen.findByTestId(`operations-purchase-row-${PURCHASE_LINE}`)
    expect((screen.getByLabelText(OPERATIONS_WORK_TEXT.filter) as HTMLInputElement).value).toBe('Tedarik')
    expect(screen.getByTestId('operations-tab-purchase').getAttribute('aria-current')).toBe('page')
    // The sales route was never read on the way.
    expect(harness.logisticsCalls().every((call) => call.url.includes('/operations-work/purchase'))).toBe(true)
  })

  it('the search narrows the shown rows only, and says so when nothing matches', async () => {
    installFetch({ capabilities: BOTH, onSalesWork: salesPage })
    renderLogisticsApp('/lojistik/operasyon-isleri/satis?ara=yok-boyle-bir-sey')
    // The page is read, and the search leaves nothing to show: the filtered-empty state, not the no-data one.
    await waitFor(() => expect(document.querySelector('[data-state="empty-filtered"]')).not.toBeNull())
    expect(screen.queryByTestId('operations-sales-table')).toBeNull()
    await userEvent.clear(screen.getByLabelText(OPERATIONS_WORK_TEXT.filter))
    await screen.findByTestId(`operations-sales-row-${SALES_LINE}`)
  })

  it('with only the purchase grant the sales section is neither offered nor opened', async () => {
    const harness = installFetch({ capabilities: [LOGISTICS_CAPABILITY.operationsPurchase], onPurchaseWork: purchasePage })
    renderLogisticsApp('/lojistik/operasyon-isleri/satinalma')
    const row = await screen.findByTestId(`operations-purchase-row-${PURCHASE_LINE}`)
    expect(screen.queryByTestId('operations-tab-sales')).toBeNull()
    // Without the purchase order detail grant the reference is text, not a link.
    expect(within(row).queryByRole('link')).toBeNull()
    cleanup()
    vi.unstubAllGlobals()
    const closed = installFetch({ capabilities: [LOGISTICS_CAPABILITY.operationsPurchase] })
    renderLogisticsApp('/lojistik/operasyon-isleri/satis')
    await waitFor(() => expect(closed.calls.some((call) => call.url.includes('/shell/capabilities'))).toBe(true))
    await waitFor(() => expect(screen.queryByRole('heading', { name: OPERATIONS_WORK_TEXT.heading })).toBeNull())
    expect(closed.logisticsCalls()).toHaveLength(0)
    expect(harness.logisticsCalls().some((call) => call.url.includes('/operations-work/sales'))).toBe(false)
  })

  it('a purchase page answered to the sales read is refused, never shown as sales work', async () => {
    installFetch({ capabilities: BOTH, onSalesWork: purchasePage })
    renderLogisticsApp('/lojistik/operasyon-isleri/satis')
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByText('Sentetik Tedarikçi')).toBeNull()
    expect(screen.queryByTestId('operations-sales-table')).toBeNull()
    expect(screen.queryByTestId(`operations-purchase-row-${PURCHASE_LINE}`)).toBeNull()
  })

  it('a forbidden section read shows the refusal and keeps nothing', async () => {
    const harness = installFetch({ capabilities: BOTH, onSalesWork: () => json({ status: 403, title: 'Forbidden' }, 403) })
    renderLogisticsApp('/lojistik/operasyon-isleri/satis')
    await waitFor(() => expect(harness.logisticsCalls().some((call) => call.url.includes('/operations-work/sales'))).toBe(true))
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).toBeNull())
    expect(screen.queryByTestId('operations-sales-table')).toBeNull()
    expect(screen.queryByTestId(`operations-sales-row-${SALES_LINE}`)).toBeNull()
  })
})

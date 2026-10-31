/**
 * UI-80 "Satış raporları" in the real shell (`AppShell`, `CapabilityRoute`) against wire bodies pinned to API-41 / API-42's schemas
 * (./examples). Component evidence, NOT_ENV: the route opens only on the id the backend publishes (Sales shell 1.11); every read is
 * company-bound; the filters are the server's; loading, empty, forbidden, unavailable, malformed, "too wide" and "kaynak yok" each
 * have their state; nothing is summed, converted or recomputed on the screen.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import openOrdersResponse from './examples/open-orders-report-response.json'
import lostResponse from './examples/lost-opportunities-report-response.json'
import priceVarianceResponse from './examples/price-variance-report-response.json'
import { SALES_CAPABILITY } from './capabilities'
import { REPORT_TEXT } from './reportText'
import { COMPANY_A, installPoolFetch, json, type PoolCall } from './poolFixtures'
import { renderPool } from './poolTestHarness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const T = REPORT_TEXT
const GRANTED = [SALES_CAPABILITY.reportsView, SALES_CAPABILITY.draftsDetail]
const notFound = (call: PoolCall): Response => {
  throw new Error(`unexpected ${call.method} ${call.url}`)
}
const problem = (status: number, code?: string) =>
  json(code === undefined ? { status } : { status, code }, status, { 'Content-Type': 'application/problem+json' })
const clone = <V,>(value: V): V => JSON.parse(JSON.stringify(value)) as V

function reports(answer: { open?: () => Response; lost?: () => Response; variance?: () => Response }) {
  return (call: PoolCall): Response => {
    if (call.url.includes('/sales/reports/open-orders')) return answer.open?.() ?? json(openOrdersResponse)
    if (call.url.includes('/sales/reports/lost-opportunities')) return answer.lost?.() ?? json(lostResponse)
    if (call.url.includes('/sales/reports/price-variance')) return answer.variance?.() ?? json(priceVarianceResponse)
    return notFound(call)
  }
}

describe('route gate (Sales shell 1.11)', () => {
  it('closes the route onto the neutral block and sends no report read when the shell does not grant the id', async () => {
    const fetch = installPoolFetch(notFound, [SALES_CAPABILITY.draftsList])
    renderPool('/satis/raporlar')
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(fetch.calls).toHaveLength(0)
  })
})

describe('Açık siparişler (API-41, KPI No 124)', () => {
  it('shows the server groups per customer, product and unit — two units stay two rows, nothing is summed on the screen', async () => {
    const fetch = installPoolFetch(reports({}), GRANTED)
    renderPool('/satis/raporlar')
    const table = await screen.findByRole('table', { name: T.openOrders.caption })
    expect(screen.getByRole('heading', { level: 1, name: T.heading })).toBeTruthy()
    expect(screen.getByRole('tab', { name: T.tab['open-orders'] }).getAttribute('aria-selected')).toBe('true')
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(3)
    expect(within(rows[0]).getByText('250.5 kg')).toBeTruthy()
    expect(within(rows[0]).getByText('3500.5 kg')).toBeTruthy()
    expect(within(rows[1]).getByText('10 adet')).toBeTruthy()
    // Sinan 2026-10-31 (EK19 pending): the quantity awaiting customer acceptance is its own column, never in the open tonnage.
    expect(within(table).getByRole('columnheader', { name: T.openOrders.awaitingAcceptance })).toBeTruthy()
    expect(within(rows[0]).getByText('800 kg')).toBeTruthy()
    expect(screen.getByTestId('sales-report-line-awaiting-c3c3c3c3-0000-4000-8000-000000000501').textContent).toContain('Rev.0')
    expect(screen.getAllByText(T.openOrders.noAcceptedRevision).length).toBeGreaterThan(0)
    // A group too large to measure shows no sums and asks to narrow the dates; it has no detail table.
    expect(within(rows[2]).getByText(T.openOrders.tooManyOrders(1500))).toBeTruthy()
    expect(within(rows[2]).queryByText(/^\d[\d.]* kg$/)).toBeNull()
    // The kg and the piece group are never added into one number.
    expect(screen.queryByText(/2290\.5|2250\.5 adet/)).toBeNull()
    // A group with an unmeasured line says so; its detail names the line and why.
    expect(within(rows[0]).getByText(T.openOrders.incomplete)).toBeTruthy()
    expect(within(rows[1]).queryByText(T.openOrders.incomplete)).toBeNull()
    expect(screen.getByText(/SO-2026-000009 \/ 2 — /)).toBeTruthy()
    expect(screen.getByText(/SO-2026-000009 \/ 2 — Revizyon yeniden onay bekliyor/)).toBeTruthy()
    // SAT-D130: a line completed within the SAT-D5 tolerance shows 0 open with its reason; an unreadable tolerance is unmeasured.
    expect(screen.getByTestId('sales-report-line-tolerance-c3c3c3c3-0000-4000-8000-000000000201').textContent).toBe(
      T.openOrders.completedWithinTolerance,
    )
    expect(screen.getByText(/SO-2026-000010 \/ 1 — Eksik sevk edildi ve teslim toleransı/)).toBeTruthy()
    // The order line opens Sipariş 360 only with that screen's grant.
    expect(screen.getAllByRole('link', { name: 'Sipariş SO-2026-000007 / 1' })[0].getAttribute('href')).toBe(
      '/satis/siparisler/c3c3c3c3-0000-4000-8000-000000000001',
    )
    expect(fetch.calls).toHaveLength(1)
    expect(fetch.calls[0].companyId).toBe(COMPANY_A)
    expect(fetch.calls[0].url).toMatch(/\/sales\/reports\/open-orders\?pageSize=50$/)
  })

  it('sends the date range and one customer to the server and pages by the server cursor', async () => {
    const user = userEvent.setup()
    const last = clone(openOrdersResponse)
    last.groups = [last.groups[1]]
    last.groups[0].productId = '22222222-2222-2222-2222-222222222223'
    last.nextCursor = null as unknown as string
    const fetch = installPoolFetch(reports({ open: () => json(fetch.calls.at(-1)?.url.includes('cursor=') ? last : openOrdersResponse) }), GRANTED)
    const { router } = renderPool('/satis/raporlar')
    await screen.findByRole('table', { name: T.openOrders.caption })
    await user.click(screen.getByRole('button', { name: /Daha fazla/ }))
    await waitFor(() => expect(document.querySelectorAll('[data-group-id]')).toHaveLength(4))
    expect(fetch.calls[1].url).toContain('cursor=c2FsZXMtcmVwb3J0LWN1cnNvcg')

    await user.type(screen.getByLabelText(T.filter.from), '2026-10-01')
    await user.type(screen.getByLabelText(T.filter.to), '2026-10-31')
    await user.click(screen.getByRole('button', { name: T.filter.apply }))
    await waitFor(() => expect(fetch.calls.at(-1)?.url).toContain('from=2026-10-01&to=2026-10-31'))
    expect(router.state.location.search).toBe('?rapor=open-orders&baslangic=2026-10-01&bitis=2026-10-31')

    await user.click(screen.getAllByRole('button', { name: T.filter.onlyCustomer })[0])
    await waitFor(() => expect(fetch.calls.at(-1)?.url).toContain('companyAccountId=44444444-4444-4444-8444-444444444444'))
    expect(await screen.findByText(T.filter.filteredTo)).toBeTruthy()
  })

  it('refuses a reversed date range on the screen and sends nothing for it', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch(reports({}), GRANTED)
    renderPool('/satis/raporlar')
    await screen.findByRole('table', { name: T.openOrders.caption })
    await user.type(screen.getByLabelText(T.filter.from), '2026-10-31')
    await user.type(screen.getByLabelText(T.filter.to), '2026-10-01')
    expect(screen.getByText(T.filter.rangeInvalid)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.filter.apply }) as HTMLButtonElement).disabled).toBe(true)
    expect(fetch.calls).toHaveLength(1)
  })

  it('continues a bounded scan that found nothing on this page, and shows 403, 503, malformed and empty as their own states', async () => {
    const user = userEvent.setup()
    // The server's work bound ended the page before an open group: a cursor and no group — "continue", never "Kayıt yok".
    const fetch = installPoolFetch(reports({
      open: () => json(fetch.calls.length > 1 ? openOrdersResponse : { measure: 'ApprovedMinusShipped', groups: [], nextCursor: 'c2Nhbi1tb3Jl' }),
    }), GRANTED)
    renderPool('/satis/raporlar')
    expect((await screen.findByTestId('sales-report-open-orders-scan-continues')).textContent).toBe(T.openOrders.scanContinues)
    expect(screen.queryByText(/Kayıt yok/)).toBeNull()
    await user.click(screen.getByRole('button', { name: /Daha fazla/ }))
    await waitFor(() => expect(fetch.calls.at(-1)?.url).toContain('cursor=c2Nhbi1tb3Jl'))
    cleanup()
    vi.unstubAllGlobals()

    const foreignUnit = clone(openOrdersResponse)
    foreignUnit.groups[0].complete = true
    for (const [answer, title] of [
      [() => problem(403), /Yetkiniz yok|Bulunamadı — sayfa/],
      [() => problem(503), /Servis geçici olarak kullanılamıyor/],
      // "complete" while a line is unmeasured contradicts itself: malformed, never shown.
      [() => json(foreignUnit), /Beklenmeyen yanıt/],
      [() => json({ measure: 'ApprovedMinusShipped', groups: [], nextCursor: null }), /Kayıt yok/],
    ] as const) {
      installPoolFetch(reports({ open: answer }), GRANTED)
      renderPool('/satis/raporlar')
      expect(await screen.findByText(title)).toBeTruthy()
      expect(screen.queryByRole('table', { name: T.openOrders.caption })).toBeNull()
      cleanup()
      vi.unstubAllGlobals()
    }
  })
})

describe('Kayıp fırsatlar (API-42, SAT-D25)', () => {
  it('lists the lost quotes with the list reason, the explanation and the net amount in the quote currency', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch(reports({}), GRANTED)
    const { router } = renderPool('/satis/raporlar')
    await screen.findByRole('table', { name: T.openOrders.caption })
    await user.click(screen.getByRole('tab', { name: T.tab.lost }))
    const table = await screen.findByRole('table', { name: T.lost.caption })
    expect(router.state.location.search).toBe('?rapor=lost')
    expect(within(table).getByText('Sentetik fiyat (SENTETIK-FIYAT)')).toBeTruthy()
    expect(within(table).getByText('SENTETIK rakip daha ucuz')).toBeTruthy()
    expect(within(table).getByText('14840.25')).toBeTruthy()
    // A loss before SAT-D25 keeps its free text; an incomplete amount is no number.
    expect(within(table).getByText(T.lost.legacyReason)).toBeTruthy()
    expect(within(table).getByText(/Eksik — fiyatsız/)).toBeTruthy()
    expect(screen.queryByTestId('sales-report-lost-withheld')).toBeNull()
    expect(fetch.calls.at(-1)?.url).toMatch(/\/sales\/reports\/lost-opportunities\?pageSize=50$/)
  })

  it('withholds every amount without the price read and refuses an amount that arrives anyway', async () => {
    const withheld = clone(lostResponse) as unknown as { amountDisclosure: string; items: { amount: unknown }[] }
    withheld.amountDisclosure = 'Withheld'
    for (const item of withheld.items) item.amount = null
    installPoolFetch(reports({ lost: () => json(withheld) }), GRANTED)
    renderPool('/satis/raporlar?rapor=lost')
    expect((await screen.findByTestId('sales-report-lost-withheld')).textContent).toBe(T.lost.withheld)
    // Each row says its own disclosure: "Gizli", never an amount.
    expect(screen.getAllByText(T.lost.withheldCell)).toHaveLength(2)
    cleanup()
    vi.unstubAllGlobals()

    const leaking = clone(lostResponse) as unknown as { amountDisclosure: string }
    leaking.amountDisclosure = 'Withheld'
    installPoolFetch(reports({ lost: () => json(leaking) }), GRANTED)
    renderPool('/satis/raporlar?rapor=lost')
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByText('14840.25')).toBeNull()
  })
})

describe('Fiyat sapması (API-42, No 25)', () => {
  it('says "kaynak yok" — never an empty list — while no invoice owner exists', async () => {
    const fetch = installPoolFetch(reports({}), GRANTED)
    renderPool('/satis/raporlar?rapor=price-variance')
    expect((await screen.findByTestId('sales-report-price-variance-no-source')).textContent).toBe(T.priceVariance.noSource)
    expect(screen.queryByText(T.priceVariance.empty)).toBeNull()
    expect(fetch.calls.at(-1)?.url).toMatch(/\/sales\/reports\/price-variance\?pageSize=50$/)
  })

  it('refuses a "NoSource" answer carrying a list, and shows the 403 of a missing price read as its own state', async () => {
    installPoolFetch(reports({ variance: () => json({ state: 'NoSource', items: [], nextCursor: null }) }), GRANTED)
    renderPool('/satis/raporlar?rapor=price-variance')
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    installPoolFetch(reports({ variance: () => problem(403) }), GRANTED)
    renderPool('/satis/raporlar?rapor=price-variance')
    expect(await screen.findByText(/Yetkiniz yok|Bulunamadı — sayfa/)).toBeTruthy()
    expect(screen.queryByTestId('sales-report-price-variance-no-source')).toBeNull()
  })
})

describe('tabs (WAI-ARIA)', () => {
  it('moves between the three reports with the arrow keys and keeps each panel labelled by its tab', async () => {
    const user = userEvent.setup()
    installPoolFetch(reports({}), GRANTED)
    renderPool('/satis/raporlar')
    await screen.findByRole('table', { name: T.openOrders.caption })
    const tabs = screen.getAllByRole('tab')
    expect(tabs.map((tab) => tab.textContent)).toEqual([T.tab['open-orders'], T.tab.lost, T.tab['price-variance']])
    // Only the selected tab controls a rendered panel; the others point at nothing.
    expect(tabs.map((tab) => tab.getAttribute('aria-controls') !== null)).toEqual([true, false, false])
    expect(document.getElementById(tabs[0].getAttribute('aria-controls') ?? '')?.getAttribute('role')).toBe('tabpanel')
    tabs[0].focus()
    await user.keyboard('{ArrowLeft}')
    expect(await screen.findByTestId('sales-report-price-variance-no-source')).toBeTruthy()
    expect(document.activeElement?.textContent).toBe(T.tab['price-variance'])
    const panel = screen.getByRole('tabpanel')
    expect(panel.getAttribute('aria-labelledby')).toBe(screen.getByRole('tab', { name: T.tab['price-variance'] }).id)
    await user.keyboard('{Home}')
    expect(await screen.findByRole('table', { name: T.openOrders.caption })).toBeTruthy()
  })
})

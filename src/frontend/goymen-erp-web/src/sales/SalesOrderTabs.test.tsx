/**
 * UI-10 / UI-11: the S-04 Sipariş 360 locked tabs (Master §7.2) and the "Sözleşme / Belgeler" tab over the Document
 * Vault. Component evidence, NOT_ENV: the API answers are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { decodeDraftDetailWithParty } from './contract'
import { DraftView } from './SalesDraftDetailPage'
import { ORDER_TAB, ORDER_TABS, parseOrderTab } from './orderTabs'
import { ORDER_TABS_TEXT } from './salesText'
import { COMPANY_A, HARNESS_PARTY_DISPLAY, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const DOCUMENT_LIMITS = {
  maximumBytes: 20_971_520,
  extensions: ['.pdf', '.png', '.jpg', '.jpeg'],
  contentTypes: ['application/pdf', 'image/png', 'image/jpeg'],
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

function isolatedDetail() {
  const raw = JSON.parse(detailResponseRaw)
  const decoded = decodeDraftDetailWithParty(
    { ...raw, partyDisplay: { partyId: raw.draft.partyId, ...HARNESS_PARTY_DISPLAY } },
    DRAFT,
  )
  expect(decoded).not.toBeNull()
  return decoded!
}

const tab = (key: string) => screen.getByRole('tab', { name: ORDER_TABS_TEXT.tab[key as keyof typeof ORDER_TABS_TEXT.tab] })

describe('S-04 locked tabs', () => {
  it('shows the six tabs in the locked order, the lines tab open first, the others hidden but mounted', () => {
    render(<DraftView detail={isolatedDetail()} canOpenParty={false} backLink={null} />)

    const names = screen.getAllByRole('tab').map((element) => element.textContent)
    expect(names).toEqual([
      'Satırlar',
      'Tahsis / Kaynak',
      'Gerçekleşme / İrsaliye',
      'Faturalar',
      'Sözleşme / Belgeler',
      'Revizyon / Geçmiş',
    ])
    expect(tab(ORDER_TAB.lines).getAttribute('aria-selected')).toBe('true')
    expect(screen.getByRole('tabpanel')).toBe(screen.getByTestId(`sales-order-tabpanel-${ORDER_TAB.lines}`))
    expect(within(screen.getByRole('tabpanel')).getByTestId('sales-line-1-facts')).toBeTruthy()
    // Hidden panels stay in the document (an in-flight command keeps its state across a tab switch).
    expect(screen.getByTestId(`sales-order-tabpanel-${ORDER_TAB.invoices}`).hidden).toBe(true)
    expect(screen.getByTestId('sales-invoices-empty').textContent).toBe(ORDER_TABS_TEXT.invoicesEmpty)
  })

  it('keeps the summary, approval and cancellation outside the tabs, so the order-level commands stay visible', () => {
    render(<DraftView detail={isolatedDetail()} canOpenParty={false} backLink={null} />)
    const tabs = screen.getByTestId('sales-order-tabs')
    expect(tabs.contains(screen.getByTestId('sales-order-summary'))).toBe(false)
    expect(tabs.contains(screen.getByTestId('sales-approval'))).toBe(false)
  })

  it('switches by click and by ←/→/Home/End with a roving tabindex (WAI-ARIA tabs)', () => {
    render(<DraftView detail={isolatedDetail()} canOpenParty={false} backLink={null} />)

    fireEvent.click(tab(ORDER_TAB.invoices))
    expect(tab(ORDER_TAB.invoices).getAttribute('aria-selected')).toBe('true')
    expect(tab(ORDER_TAB.invoices).tabIndex).toBe(0)
    expect(tab(ORDER_TAB.lines).tabIndex).toBe(-1)
    expect(within(screen.getByRole('tabpanel')).getByTestId('sales-invoices-empty')).toBeTruthy()

    fireEvent.keyDown(tab(ORDER_TAB.invoices), { key: 'ArrowRight' })
    expect(tab(ORDER_TAB.documents).getAttribute('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(tab(ORDER_TAB.documents))

    fireEvent.keyDown(tab(ORDER_TAB.documents), { key: 'End' })
    expect(tab(ORDER_TAB.history).getAttribute('aria-selected')).toBe('true')
    fireEvent.keyDown(tab(ORDER_TAB.history), { key: 'ArrowRight' })
    expect(tab(ORDER_TAB.lines).getAttribute('aria-selected')).toBe('true')
    fireEvent.keyDown(tab(ORDER_TAB.lines), { key: 'ArrowLeft' })
    expect(tab(ORDER_TAB.history).getAttribute('aria-selected')).toBe('true')
    fireEvent.keyDown(tab(ORDER_TAB.history), { key: 'Home' })
    expect(tab(ORDER_TAB.lines).getAttribute('aria-selected')).toBe('true')
  })

  it('labels every panel by its tab', () => {
    render(<DraftView detail={isolatedDetail()} canOpenParty={false} backLink={null} />)
    for (const key of ORDER_TABS) {
      const panel = screen.getByTestId(`sales-order-tabpanel-${key}`)
      const control = screen.getByTestId(`sales-order-tab-${key}`)
      expect(panel.getAttribute('aria-labelledby')).toBe(control.id)
      expect(control.getAttribute('aria-controls')).toBe(panel.id)
    }
  })

  it('reads an unknown or missing tab parameter as the first tab, never a near match', () => {
    expect(parseOrderTab(null)).toBe(ORDER_TAB.lines)
    expect(parseOrderTab('')).toBe(ORDER_TAB.lines)
    expect(parseOrderTab('Faturalar')).toBe(ORDER_TAB.lines)
    expect(parseOrderTab('fatura')).toBe(ORDER_TAB.lines)
    expect(parseOrderTab('faturalar')).toBe(ORDER_TAB.invoices)
  })
})

describe('S-04 tabs in the routed page', () => {
  it('opens the tab named by ?sekme= and writes the chosen tab back to the address', async () => {
    installFetch({ onDetail: () => json(JSON.parse(detailResponseRaw)) })
    const app = renderSalesApp(`/satis/siparisler/${DRAFT}?sekme=faturalar`)

    await screen.findByTestId('sales-order-tabs')
    expect(tab(ORDER_TAB.invoices).getAttribute('aria-selected')).toBe('true')

    fireEvent.click(tab(ORDER_TAB.history))
    await waitFor(() => expect(app.router.state.location.search).toBe('?sekme=gecmis'))

    fireEvent.click(tab(ORDER_TAB.lines))
    await waitFor(() => expect(app.router.state.location.search).toBe(''))
  })

  it('asks the vault for this sales order only once the "Sözleşme / Belgeler" tab is opened', async () => {
    const raw = JSON.parse(detailResponseRaw)
    const harness = installFetch({
      onDetail: () => json(raw),
      onDocuments: () => json({ items: [], canUpload: false, limits: DOCUMENT_LIMITS }),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-order-tabs')
    expect(harness.calls.some((call) => call.url.includes('/documents?'))).toBe(false)

    fireEvent.click(tab(ORDER_TAB.documents))

    await waitFor(() => expect(harness.calls.some((call) => call.url.includes('/documents?'))).toBe(true))
    const read = harness.calls.find((call) => call.url.includes('/documents?'))!
    const query = new URLSearchParams(read.url.slice(read.url.indexOf('?') + 1))
    expect(query.get('ownerKind')).toBe('sales.order')
    expect(query.get('ownerId')).toBe(raw.draft.id)
    expect(read).toMatchObject({ method: 'GET', companyId: COMPANY_A })
    const panel = screen.getByTestId(`sales-order-tabpanel-${ORDER_TAB.documents}`)
    expect(await within(panel).findByRole('heading', { name: 'Belgeler' })).toBeTruthy()
  })
})

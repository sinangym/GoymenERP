/**
 * API-09 / API-36 (PR #986) in the S-04 "Faturalar" tab, routed through the real shell. Component evidence (NOT_ENV, SENTETIK):
 * the server answers are the harness's. Proves: the order's invoices are read only once the tab is opened, under the active
 * company; the empty, more-available, service-unavailable (with retry) and malformed states are explicit; the finalization
 * panel shows the server's state, Finance stage and instalment plan verbatim; the open policies are said neutrally; and no
 * finalize / re-send control is offered while the shell publishes no capability for it.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_CAPABILITY } from './capabilities'
import { FINANCE_STAGE_TEXT, INVOICE_FINALIZATION_TEXT as T, INVOICE_STATE_TEXT, ORDER_INVOICES_TEXT as L } from './invoiceText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp, type RecordedCall } from './testHarness'
import {
  INVOICE_ID,
  INVOICE_NO,
  OPEN_ITEM_2,
  admitted,
  draftInvoiceItem,
  finalizedDetail,
  invoiceDetail,
  matchOf,
  sentWaiting,
} from './invoiceTestData'

const RAW = JSON.parse(detailResponseRaw)
const DRAFT: string = RAW.draft.id
const PATH = `/satis/siparisler/${DRAFT}?sekme=faturalar`
const WITH_INVOICE_READ = [...ALL_SALES, SALES_CAPABILITY.invoiceCandidatesList]

const invoiceListReads = (calls: readonly RecordedCall[]) =>
  calls.filter((call) => call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/invoices$/.test(call.url))
const invoiceReads = (calls: readonly RecordedCall[]) =>
  calls.filter((call) => call.method === 'GET' && /\/sales\/invoices\/[^/?]+$/.test(call.url))

async function invoicesSection() {
  return (await screen.findByTestId('sales-order-invoices')) as HTMLElement
}

async function openPanel() {
  fireEvent.click(await screen.findByTestId(`sales-order-invoice-open-${INVOICE_ID}`))
  const panel = await screen.findByTestId('sales-invoice-finalization')
  await within(panel).findByRole('heading', { name: T.heading(INVOICE_NO) })
  return panel
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

describe('API-09 order invoices tab', () => {
  it('reads the invoices only once the tab is opened, under the active company, and lists them', async () => {
    const harness = installFetch({
      capabilities: WITH_INVOICE_READ,
      onDetail: () => json(RAW),
      onDraftInvoices: () => json({ items: [draftInvoiceItem()], hasMore: false }),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-order-tabs')
    expect(invoiceListReads(harness.calls)).toHaveLength(0)

    fireEvent.click(screen.getByTestId('sales-order-tab-faturalar'))
    const section = await invoicesSection()
    const table = await within(section).findByRole('table', { name: L.caption })
    const row = within(table).getByTestId(`sales-order-invoice-${INVOICE_ID}`)
    expect(within(row).getByRole('rowheader').textContent).toBe(INVOICE_NO)
    expect(row.textContent).toContain(INVOICE_STATE_TEXT.Approved)
    expect(row.textContent).toContain(L.lineCountValue(2, 3))
    const reads = invoiceListReads(harness.calls)
    expect(reads).toHaveLength(1)
    expect(reads[0].companyId).toBe(COMPANY_A)
    expect(reads[0].url).toContain(`/sales/drafts/${DRAFT}/invoices`)
    expect(invoiceReads(harness.calls)).toHaveLength(0)
    expect(screen.queryByTestId('sales-order-invoices-more')).toBeNull()
  })

  it('says when the order has no invoice yet, and when the server has more than it listed', async () => {
    installFetch({ capabilities: WITH_INVOICE_READ, onDetail: () => json(RAW) })
    renderSalesApp(PATH)
    expect((await screen.findByTestId('sales-order-invoices-empty')).textContent).toBe(L.empty)
    cleanup()
    vi.unstubAllGlobals()

    installFetch({
      capabilities: WITH_INVOICE_READ,
      onDetail: () => json(RAW),
      onDraftInvoices: () => json({ items: [draftInvoiceItem()], hasMore: true }),
    })
    renderSalesApp(PATH)
    expect((await screen.findByTestId('sales-order-invoices-more')).textContent).toBe(L.hasMore)
  })

  it('shows the service-unavailable state with a retry that reads again', async () => {
    let answer = () => json({ status: 503, title: 'Service Unavailable' }, 503)
    const harness = installFetch({ capabilities: WITH_INVOICE_READ, onDetail: () => json(RAW), onDraftInvoices: () => answer() })
    renderSalesApp(PATH)
    const section = await invoicesSection()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('service-unavailable')
    answer = () => json({ items: [draftInvoiceItem()], hasMore: false })
    fireEvent.click(within(alert).getByRole('button', { name: 'Tekrar dene' }))
    await within(section).findByTestId(`sales-order-invoice-${INVOICE_ID}`)
    expect(invoiceListReads(harness.calls)).toHaveLength(2)
  })

  it('refuses a malformed list instead of showing part of it', async () => {
    installFetch({
      capabilities: WITH_INVOICE_READ,
      onDetail: () => json(RAW),
      onDraftInvoices: () => json({ items: [draftInvoiceItem({ state: 'Open' })], hasMore: false }),
    })
    renderSalesApp(PATH)
    const section = await invoicesSection()
    expect((await within(section).findByRole('alert')).getAttribute('data-state')).toBe('malformed-response')
    expect(within(section).queryByRole('table')).toBeNull()
  })

  it('offers no detail without the invoice read capability, and says why', async () => {
    const harness = installFetch({
      capabilities: ALL_SALES,
      onDetail: () => json(RAW),
      onDraftInvoices: () => json({ items: [draftInvoiceItem()], hasMore: false }),
    })
    renderSalesApp(PATH)
    const section = await invoicesSection()
    await within(section).findByTestId(`sales-order-invoice-${INVOICE_ID}`)
    expect(within(section).queryByTestId(`sales-order-invoice-open-${INVOICE_ID}`)).toBeNull()
    expect(within(section).getByTestId('sales-order-invoices-no-detail').textContent).toBe(L.noDetailRead)
    expect(invoiceReads(harness.calls)).toHaveLength(0)
  })
})

describe('API-36 finalization panel in the tab', () => {
  it('shows an approved invoice: the match, the planned instalments verbatim, and no control without the capability', async () => {
    const harness = installFetch({
      capabilities: WITH_INVOICE_READ,
      onDetail: () => json(RAW),
      onDraftInvoices: () => json({ items: [draftInvoiceItem()], hasMore: false }),
      onInvoice: () => json(invoiceDetail({ match: matchOf({ difference: '-0.10' }) })),
    })
    renderSalesApp(PATH)
    const panel = await openPanel()
    expect(within(panel).getByTestId('sales-invoice-state').textContent).toBe(INVOICE_STATE_TEXT.Approved)
    expect(within(panel).getByTestId('sales-invoice-payable').textContent).toBe('1180.40')
    expect(within(panel).getByTestId('sales-invoice-match-difference').textContent).toBe('-0.10')
    expect(within(panel).getByTestId('sales-invoice-not-finalized').textContent).toBe(T.notFinalized)
    const table = within(panel).getByRole('table', { name: T.instalmentsPlannedCaption })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => Array.from(row.querySelectorAll('th, td')).map((cell) => cell.textContent))).toEqual([
      ['1', '01.12.2026', '33.3333', '393.47'],
      ['2', '01.01.2027', '66.6667', '786.93'],
    ])
    expect(within(panel).getByTestId('sales-invoice-controls-unavailable').textContent).toBe(T.controlsUnavailable)
    expect(within(panel).queryByTestId('sales-invoice-finalize')).toBeNull()
    expect(within(panel).queryByTestId('sales-invoice-resend')).toBeNull()
    const reads = invoiceReads(harness.calls)
    expect(reads).toHaveLength(1)
    expect(reads[0].companyId).toBe(COMPANY_A)
    expect(harness.calls.filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('shows a finalized invoice Finance has not answered yet, with the final plan and the neutral re-send policy note', async () => {
    installFetch({
      capabilities: WITH_INVOICE_READ,
      onDetail: () => json(RAW),
      onDraftInvoices: () => json({ items: [draftInvoiceItem({ state: 'Finalized' })], hasMore: false }),
      onInvoice: () => json(finalizedDetail(sentWaiting('unavailable'))),
    })
    renderSalesApp(PATH)
    const panel = await openPanel()
    expect(within(panel).getByTestId('sales-invoice-state').textContent).toBe(INVOICE_STATE_TEXT.Finalized)
    expect(within(panel).getByTestId('sales-invoice-finance-stage').textContent).toBe(`${T.financeStage}: ${FINANCE_STAGE_TEXT['sent-waiting']}`)
    expect(within(panel).getByTestId('sales-invoice-attempt-count').textContent).toBe('2')
    expect(within(panel).getByTestId('sales-invoice-last-outcome').textContent).toBe('unavailable')
    expect(within(panel).getByTestId('sales-invoice-accounting').textContent).toBe(T.accountingNoSource)
    expect(within(panel).getByTestId('sales-invoice-redrive-note').textContent).toBe(T.redriveNote)
    expect(within(panel).queryByTestId('sales-invoice-denied-note')).toBeNull()
    const table = within(panel).getByRole('table', { name: T.instalmentsFinalCaption })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => Array.from(row.querySelectorAll('th, td')).map((cell) => cell.textContent))).toEqual([
      ['1', '01.12.2026', '393.47', T.functionalMissing, 'f2000000-0000-4000-8000-000000000001'],
      ['2', '01.01.2027', '786.93', '25210.77', OPEN_ITEM_2],
    ])
    expect(within(panel).getByTestId('sales-invoice-controls-unavailable')).toBeTruthy()
  })

  it('says Finance refused on authority grounds without offering a way out, and shows an accepted invoice without notes', async () => {
    let body = finalizedDetail(sentWaiting('refused:denied'))
    installFetch({
      capabilities: WITH_INVOICE_READ,
      onDetail: () => json(RAW),
      onDraftInvoices: () => json({ items: [draftInvoiceItem({ state: 'Finalized' })], hasMore: false }),
      onInvoice: () => json(body),
    })
    renderSalesApp(PATH)
    let panel = await openPanel()
    expect(within(panel).getByTestId('sales-invoice-finance-stage').textContent).toContain(FINANCE_STAGE_TEXT.refused)
    expect(within(panel).getByTestId('sales-invoice-denied-note').textContent).toBe(T.deniedNote)
    expect(within(panel).queryByTestId('sales-invoice-redrive-note')).toBeNull()

    body = finalizedDetail(admitted())
    fireEvent.click(screen.getByTestId(`sales-order-invoice-open-${INVOICE_ID}`))
    await waitFor(() => expect(screen.queryByTestId('sales-invoice-finalization')).toBeNull())
    panel = await openPanel()
    expect(within(panel).getByTestId('sales-invoice-finance-stage').textContent).toContain(FINANCE_STAGE_TEXT.accepted)
    expect(within(panel).queryByTestId('sales-invoice-denied-note')).toBeNull()
    expect(within(panel).queryByTestId('sales-invoice-redrive-note')).toBeNull()
    expect(within(panel).queryByTestId('sales-invoice-controls-unavailable')).toBeNull()
  })

  it('refuses a malformed detail and another invoice, and shows not-found for a 404', async () => {
    let answer = () => json(invoiceDetail({ state: 'Finalized' }))
    installFetch({
      capabilities: WITH_INVOICE_READ,
      onDetail: () => json(RAW),
      onDraftInvoices: () => json({ items: [draftInvoiceItem()], hasMore: false }),
      onInvoice: () => answer(),
    })
    renderSalesApp(PATH)
    fireEvent.click(await screen.findByTestId(`sales-order-invoice-open-${INVOICE_ID}`))
    let panel = await screen.findByTestId('sales-invoice-finalization')
    expect((await within(panel).findByRole('alert')).getAttribute('data-state')).toBe('malformed-response')
    expect(within(panel).queryByTestId('sales-invoice-instalments')).toBeNull()

    answer = () => json(invoiceDetail({ id: 'f1000000-0000-4000-8000-000000000009' }))
    fireEvent.click(within(panel).getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(within(panel).getByRole('alert').getAttribute('data-state')).toBe('malformed-response'))

    answer = () => json({ status: 404, title: 'Not Found' }, 404)
    fireEvent.click(screen.getByTestId(`sales-order-invoice-open-${INVOICE_ID}`))
    fireEvent.click(screen.getByTestId(`sales-order-invoice-open-${INVOICE_ID}`))
    panel = await screen.findByTestId('sales-invoice-finalization')
    await waitFor(() => expect(within(panel).getByRole('alert').getAttribute('data-state')).toBe('not-found'))
  })
})

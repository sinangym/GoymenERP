/**
 * UI-63 S-09 "Satış faturaları" in the real shell (`AppShell`, `CapabilityRoute`) against wire bodies pinned to API-32's
 * `SalesInvoiceListView`. Component evidence, NOT_ENV: the route opens only on `sales.invoices.list` (Sales shell 1.19); every
 * read is company-bound; loading, empty, forbidden, unavailable and malformed answers each have their state; paging follows
 * the server cursor; a row opens the existing invoice detail read-only.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SALES_CAPABILITY } from './capabilities'
import { INVOICE_LIST_TEXT } from './invoiceListText'
import { INVOICE_ID, INVOICE_NO, OTHER_INVOICE_ID, invoiceDetail } from './invoiceTestData'
import { ACCOUNT, COMPANY_A, CURRENCY, PARTY, installPoolFetch, json, type PoolCall } from './poolFixtures'
import { renderPool } from './poolTestHarness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const T = INVOICE_LIST_TEXT
const GRANTED = [SALES_CAPABILITY.invoicesList]
const ROUTE = '/satis/faturalar'
type Wire = Record<string, unknown>

const summary = (over: Wire = {}): Wire => ({
  id: INVOICE_ID,
  documentNo: INVOICE_NO,
  state: 'Approved',
  version: '4',
  businessDate: '2026-11-01',
  partyId: PARTY,
  companyAccountId: ACCOUNT,
  currencyId: CURRENCY,
  amountDisclosure: 'Visible',
  payableAmount: '1180.40',
  functionalPayableAmount: null,
  lineCount: 2,
  createdAtUtc: '2026-11-01T08:00:00Z',
  ...over,
})
const page = (items: Wire[], nextCursor: string | null = null) => ({ items, nextCursor })
const notFound = (call: PoolCall): Response => {
  throw new Error(`unexpected ${call.method} ${call.url}`)
}
const problem = (status: number) => json({ status }, status, { 'Content-Type': 'application/problem+json' })
const isList = (call: PoolCall) => call.url.includes('/sales/invoices?')

describe('UI-63 route gate (Sales shell 1.19)', () => {
  it('closes the route onto the neutral block and sends no Sales read without sales.invoices.list', async () => {
    const fetch = installPoolFetch(notFound, [SALES_CAPABILITY.invoiceCandidatesList])
    renderPool(ROUTE)
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(fetch.calls).toHaveLength(0)
  })
})

describe('UI-63 S-09 Satış faturaları', () => {
  it('lists the invoices through one company-bound read of the server page, amounts as the server wrote them', async () => {
    const second = summary({
      id: OTHER_INVOICE_ID,
      documentNo: 'SF-2026-0006',
      state: 'Finalized',
      payableAmount: '250.00',
      functionalPayableAmount: '8750.00',
      lineCount: 1,
    })
    const fetch = installPoolFetch((call) => (isList(call) ? json(page([summary(), second])) : notFound(call)), GRANTED)
    renderPool(ROUTE)
    const table = await screen.findByRole('table', { name: T.caption })
    expect(screen.getByRole('heading', { level: 1, name: T.heading })).toBeTruthy()
    expect(within(table).getByRole('button', { name: T.openLabel(INVOICE_NO) })).toBeTruthy()
    expect(within(table).getByText('Onaylandı')).toBeTruthy()
    expect(within(table).getByText('Kesinleşti')).toBeTruthy()
    expect(within(table).getByText('1180.40')).toBeTruthy()
    expect(within(table).getByText('8750.00')).toBeTruthy()
    expect(within(table).getByText(T.none)).toBeTruthy()
    // The server's order is kept: no client sort.
    const ids = [...document.querySelectorAll('[data-invoice-id]')].map((row) => row.getAttribute('data-invoice-id'))
    expect(ids).toEqual([INVOICE_ID, OTHER_INVOICE_ID])
    // No command on the list.
    expect(screen.queryByRole('button', { name: /Kesinleştir|Onayla|İptal/ })).toBeNull()
    expect(fetch.calls).toHaveLength(1)
    expect(fetch.calls[0].companyId).toBe(COMPANY_A)
    expect(fetch.calls[0].url).toBe('/api/v1/sales/invoices?pageSize=50')
  })

  it('shows the amounts the server withheld (SAT-D238) as "Gizli", never as 0 or "—"', async () => {
    const withheld = summary({
      id: OTHER_INVOICE_ID,
      documentNo: 'SF-2026-0006',
      amountDisclosure: 'Withheld',
      payableAmount: null,
      functionalPayableAmount: null,
    })
    installPoolFetch((call) => (isList(call) ? json(page([summary(), withheld])) : notFound(call)), GRANTED)
    renderPool(ROUTE)
    const table = await screen.findByRole('table', { name: T.caption })
    expect(within(table).getByText('1180.40')).toBeTruthy()
    const payable = screen.getByTestId(`sales-invoices-payable-withheld-${OTHER_INVOICE_ID}`)
    const functional = screen.getByTestId(`sales-invoices-functional-withheld-${OTHER_INVOICE_ID}`)
    for (const cell of [payable, functional]) {
      expect(cell.textContent).toBe(T.withheld)
      expect(cell.getAttribute('aria-label')).toBe(T.withheldLabel)
    }
    expect(screen.queryByTestId(`sales-invoices-payable-withheld-${INVOICE_ID}`)).toBeNull()
    const withheldRow = document.querySelector(`[data-invoice-id="${OTHER_INVOICE_ID}"]`) as HTMLElement
    expect(within(withheldRow).queryByText(/^0(?:[.,]0+)?$/)).toBeNull()
    expect(within(withheldRow).queryByText(T.none)).toBeNull()
  })

  it('pages by the server cursor and never appends a row twice', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch((call) => {
      if (call.url.includes('cursor=')) {
        return json(page([summary(), summary({ id: OTHER_INVOICE_ID, documentNo: 'SF-2026-0006' })]))
      }
      return isList(call) ? json(page([summary()], 'c2FsZXMtaW52b2ljZQ')) : notFound(call)
    }, GRANTED)
    renderPool(ROUTE)
    await screen.findByRole('table', { name: T.caption })
    await user.click(screen.getByRole('button', { name: /Daha fazla/ }))
    await waitFor(() => expect(document.querySelectorAll('[data-invoice-id]')).toHaveLength(2))
    expect(fetch.calls[1].url).toBe('/api/v1/sales/invoices?pageSize=50&cursor=c2FsZXMtaW52b2ljZQ')
    expect(screen.queryByRole('button', { name: /Daha fazla/ })).toBeNull()
  })

  it('keeps the shown rows when the next page fails and retries the same cursor', async () => {
    const user = userEvent.setup()
    let failNext = true
    const fetch = installPoolFetch((call) => {
      if (call.url.includes('cursor=')) {
        if (failNext) {
          failNext = false
          return problem(503)
        }
        return json(page([summary({ id: OTHER_INVOICE_ID, documentNo: 'SF-2026-0006' })]))
      }
      return isList(call) ? json(page([summary()], 'next-1')) : notFound(call)
    }, GRANTED)
    renderPool(ROUTE)
    await screen.findByRole('table', { name: T.caption })
    await user.click(screen.getByRole('button', { name: /Daha fazla/ }))
    expect(await screen.findByText(/Servis geçici olarak kullanılamıyor/)).toBeTruthy()
    expect(document.querySelectorAll('[data-invoice-id]')).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: /Tekrar dene/ }))
    await waitFor(() => expect(document.querySelectorAll('[data-invoice-id]')).toHaveLength(2))
    expect(fetch.calls.filter((call) => call.url.includes('cursor=next-1'))).toHaveLength(2)
  })

  it('shows "Kayıt yok" for an empty company list', async () => {
    installPoolFetch((call) => (isList(call) ? json(page([])) : notFound(call)), GRANTED)
    renderPool(ROUTE)
    expect(await screen.findByText(/Kayıt yok/)).toBeTruthy()
    expect(screen.queryByRole('table', { name: T.caption })).toBeNull()
  })

  it.each([
    [403, /Yetkiniz yok|Bulunamadı — sayfa/],
    [503, /Servis geçici olarak kullanılamıyor/],
  ])('shows the %s answer as its own state, never as an empty list', async (status, title) => {
    installPoolFetch((call) => (isList(call) ? problem(status) : notFound(call)), GRANTED)
    renderPool(ROUTE)
    expect(await screen.findByText(title)).toBeTruthy()
    expect(screen.queryByText(/Kayıt yok/)).toBeNull()
    expect(screen.queryByRole('table', { name: T.caption })).toBeNull()
  })

  it('reports a 401 to the auth boundary and shows no rows', async () => {
    installPoolFetch((call) => (isList(call) ? problem(401) : notFound(call)), GRANTED)
    renderPool(ROUTE)
    await waitFor(() => expect(screen.queryByRole('table', { name: T.caption })).toBeNull())
    expect(await screen.findByText(/Oturum|giriş/i)).toBeTruthy()
    expect(screen.queryByText(INVOICE_NO)).toBeNull()
  })

  it.each([
    ['an unknown state', { state: 'Paid' }],
    ['a numeric amount', { payableAmount: 1180.4 }],
    ['a withheld row carrying an amount', { amountDisclosure: 'Withheld' }],
    ['an extra member', { companyId: COMPANY_A }],
    ['a negative line count', { lineCount: -1 }],
  ])('refuses a page with %s as a malformed answer', async (_label, over) => {
    installPoolFetch((call) => (isList(call) ? json(page([summary(over)])) : notFound(call)), GRANTED)
    renderPool(ROUTE)
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByText(INVOICE_NO)).toBeNull()
  })

  it('opens the existing invoice detail read-only from a row, addressed in the URL, and closes it again', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch((call) => {
      if (isList(call)) return json(page([summary()]))
      if (call.url.endsWith(`/sales/invoices/${INVOICE_ID}`)) return json(invoiceDetail())
      return notFound(call)
    }, GRANTED)
    const { router } = renderPool(ROUTE)
    await screen.findByRole('table', { name: T.caption })
    await user.click(screen.getByRole('button', { name: T.openLabel(INVOICE_NO) }))
    const panel = await screen.findByTestId('sales-invoice-finalization')
    expect(within(panel).getByTestId('sales-invoice-payable').textContent).toBe('1180.40')
    expect(within(panel).getByTestId('sales-invoice-controls-unavailable')).toBeTruthy()
    expect(router.state.location.search).toBe(`?fatura=${INVOICE_ID}`)
    const detailCall = fetch.calls.find((call) => call.url.endsWith(`/sales/invoices/${INVOICE_ID}`))
    expect(detailCall?.companyId).toBe(COMPANY_A)
    expect(fetch.posts()).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: T.close }))
    await waitFor(() => expect(screen.queryByTestId('sales-invoices-detail')).toBeNull())
    expect(router.state.location.search).toBe('')
  })

  it('opens no detail for a withheld row: the document number is plain text and a deep link shows the hint (SAT-D238)', async () => {
    const withheld = summary({ amountDisclosure: 'Withheld', payableAmount: null, functionalPayableAmount: null })
    const fetch = installPoolFetch((call) => (isList(call) ? json(page([withheld])) : notFound(call)), GRANTED)
    renderPool(ROUTE)
    await screen.findByRole('table', { name: T.caption })
    expect(screen.queryByRole('button', { name: T.openLabel(INVOICE_NO) })).toBeNull()
    const documentNo = screen.getByTestId(`sales-invoices-document-withheld-${INVOICE_ID}`)
    expect(documentNo.textContent).toBe(INVOICE_NO)
    expect(documentNo.getAttribute('title')).toBe(T.detailWithheld)
    cleanup()

    fetch.calls.length = 0
    const user = userEvent.setup()
    const { router } = renderPool(`${ROUTE}?fatura=${INVOICE_ID}`)
    const hint = await screen.findByTestId('sales-invoices-detail-withheld')
    expect(within(hint).getByText(T.detailWithheld)).toBeTruthy()
    expect(screen.queryByTestId('sales-invoice-finalization')).toBeNull()
    expect(fetch.calls.every(isList)).toBe(true)
    await user.click(within(hint).getByRole('button', { name: T.close }))
    await waitFor(() => expect(screen.queryByTestId('sales-invoices-detail-withheld')).toBeNull())
    expect(router.state.location.search).toBe('')
  })

  it('opens no detail for a deep link beyond the loaded rows once the amounts are withheld from the user (SAT-D238)', async () => {
    const withheld = summary({ amountDisclosure: 'Withheld', payableAmount: null, functionalPayableAmount: null })
    const strangerId = '9b2f3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d'
    const fetch = installPoolFetch((call) => (isList(call) ? json(page([withheld])) : notFound(call)), GRANTED)
    for (const id of [OTHER_INVOICE_ID, strangerId]) {
      fetch.calls.length = 0
      renderPool(`${ROUTE}?fatura=${id}`)
      const hint = await screen.findByTestId('sales-invoices-detail-withheld')
      expect(within(hint).getByText(T.detailWithheld)).toBeTruthy()
      expect(screen.queryByTestId('sales-invoices-detail')).toBeNull()
      expect(screen.queryByTestId('sales-invoice-finalization')).toBeNull()
      expect(fetch.calls.length).toBeGreaterThan(0)
      expect(fetch.calls.every(isList)).toBe(true)
      cleanup()
    }
  })

  it('sends no detail read for a deep link into an empty list', async () => {
    const fetch = installPoolFetch((call) => (isList(call) ? json(page([])) : notFound(call)), GRANTED)
    renderPool(`${ROUTE}?fatura=${INVOICE_ID}`)
    expect(await screen.findByText(/Kayıt yok/)).toBeTruthy()
    expect(screen.queryByTestId('sales-invoices-detail')).toBeNull()
    expect(screen.queryByTestId('sales-invoice-finalization')).toBeNull()
    expect(fetch.calls).toHaveLength(1)
    expect(fetch.calls.every(isList)).toBe(true)
  })

  it('sends no detail read before the first page has answered', async () => {
    let answerList: ((response: Response) => void) | null = null
    const fetch = installPoolFetch((call) => {
      if (isList(call)) return new Promise<Response>((resolve) => { answerList = resolve })
      return notFound(call)
    }, GRANTED)
    renderPool(`${ROUTE}?fatura=${INVOICE_ID}`)
    await waitFor(() => expect(fetch.calls.some(isList)).toBe(true))
    expect(screen.queryByTestId('sales-invoices-detail')).toBeNull()
    expect(fetch.calls.every(isList)).toBe(true)
    expect(answerList).not.toBeNull()
  })

  it('still opens a deep-linked invoice beyond the loaded rows while the amounts are visible', async () => {
    const fetch = installPoolFetch((call) => {
      if (isList(call)) return json(page([summary()]))
      if (call.url.endsWith(`/sales/invoices/${OTHER_INVOICE_ID}`)) return json(invoiceDetail({ id: OTHER_INVOICE_ID }))
      return notFound(call)
    }, GRANTED)
    renderPool(`${ROUTE}?fatura=${OTHER_INVOICE_ID}`)
    expect(await screen.findByTestId('sales-invoices-detail')).toBeTruthy()
    await waitFor(() => expect(fetch.calls.some((call) => call.url.endsWith(`/sales/invoices/${OTHER_INVOICE_ID}`))).toBe(true))
    expect(screen.queryByTestId('sales-invoices-detail-withheld')).toBeNull()
  })

  it('opens a deep-linked invoice and ignores a fatura value that is not a reference id', async () => {
    const fetch = installPoolFetch((call) => {
      if (isList(call)) return json(page([summary()]))
      if (call.url.endsWith(`/sales/invoices/${INVOICE_ID}`)) return json(invoiceDetail())
      return notFound(call)
    }, GRANTED)
    renderPool(`${ROUTE}?fatura=${INVOICE_ID}`)
    expect(await screen.findByTestId('sales-invoice-finalization')).toBeTruthy()
    cleanup()

    fetch.calls.length = 0
    renderPool(`${ROUTE}?fatura=not-an-id`)
    await screen.findByRole('table', { name: T.caption })
    expect(screen.queryByTestId('sales-invoices-detail')).toBeNull()
    expect(fetch.calls.every(isList)).toBe(true)
  })
})

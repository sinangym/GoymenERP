/**
 * D-05 "Onaylarım" in the REAL shell (router, CapabilityRoute, ShellProvider, auth boundary) over a stubbed fetch that
 * plays #639's Work read. Component evidence only: the server's authority, company fence and separation of duties are
 * proven by #639's own HTTP/SQL tests and by the real-browser acceptance, not here.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import {
  APPROVAL, COMPANY, COUNT, COUNTERPARTY, DRAFT, LABEL, SCRAP, SCRAP_LABEL,
  countDetail, countItem, detail, item, salesDetail, salesItem, scrapDetail, scrapItem,
} from './approvalInboxFixtures'
import { SALES_CAPABILITY } from '../sales/capabilities'
import {
  INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY,
  INVENTORY_STOCK_COUNT_RECORD_CAPABILITY,
  INVENTORY_STOCK_POSITIONS_CAPABILITY,
  INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY,
  INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY,
} from '../inventory/capabilities'
import { APPROVALS_TEXT, formatSignedKg } from './workText'

const USER = '11111111-1111-1111-1111-111111111111'
const OTHER_COMPANY = '10000000-0000-0000-0000-000000000002'
const DIRECTORY = [
  { id: COMPANY, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false },
  { id: OTHER_COMPANY, code: 'GSP', legalName: 'Göymen B', isPlaceholder: false },
]
const NATIVE_PATH = `/finans/resmi-fatura-aktarim-onaylari?approvalId=${APPROVAL}`
const APPROVE = CAPABILITY.financeOfficialInvoiceImportApprove

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

interface Served {
  readonly list?: (url: string, companyId: string | null) => Response
  readonly detail?: (url: string, companyId: string | null) => Response
}

function installFetch(granted: readonly string[], served: Served = {}) {
  const calls: { url: string; companyId: string | null; method: string }[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const headers = (init?.headers ?? {}) as Record<string, string>
    calls.push({ url, companyId: headers['X-Company-Id'] ?? null, method: (init?.method ?? 'GET').toUpperCase() })
    if (url.includes('/identity/me')) return json({ userId: USER, displayName: 'Onaycı', email: null, allowedCompanyIds: [COMPANY, OTHER_COMPANY] })
    if (url.includes('/organization/companies')) return json(DIRECTORY)
    if (url.includes('/shell/capabilities')) {
      return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'gen-1', capabilities: granted })
    }
    if (/\/work\/approvals\/[^/?]+\/[^/?]+/.test(url)) return (served.detail ?? (() => json(detail)))(url, headers['X-Company-Id'] ?? null)
    if (url.includes('/work/approvals')) {
      return (served.list ?? (() => json({ companyId: COMPANY, items: [item], nextCursor: null })))(url, headers['X-Company-Id'] ?? null)
    }
    return json({ code: 'unavailable' }, 503)
  }))
  return { calls, work: () => calls.filter((call) => call.url.includes('/work/approvals')) }
}

/** `staleTime`: 0 by default; 30_000 is the production ShellProvider cache timing. */
async function renderAt(path: string, staleTime = 0) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
  await screen.findByText('Onaycı', undefined, { timeout: 5000 })
  return router
}

const notFound = () => document.querySelector('[data-state="not-found"]')

beforeEach(() => {
  try {
    window.localStorage.clear()
    window.sessionStorage.clear()
  } catch {
    // no storage here
  }
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('the home entry', () => {
  it('shows "Onaylarım" only to an actor holding the native Finance approve capability, without a count', async () => {
    const { work } = installFetch([APPROVE])
    await renderAt('/')
    const entry = await screen.findByTestId('home-approvals')
    expect(within(entry).getByRole('heading', { name: APPROVALS_TEXT.homeHeading })).toBeTruthy()
    expect(within(entry).getByRole('link', { name: APPROVALS_TEXT.homeLink }).getAttribute('href')).toBe('/onaylarim')
    // The home reads nothing for it: no count, no KPI.
    expect(work()).toEqual([])
    expect(entry.textContent).not.toMatch(/\d/)
  })

  it('is absent for an actor without that capability', async () => {
    installFetch([CAPABILITY.cariPartiesList])
    await renderAt('/')
    await screen.findByRole('heading', { level: 1, name: 'Ortak kabuk hazır' })
    expect(screen.queryByTestId('home-approvals')).toBeNull()
  })
})

describe('/onaylarim — the Finance pilot inbox', () => {
  it('lists the pending Finance approvals of the committed company and links each to Finance\'s own approval screen', async () => {
    const { work } = installFetch([APPROVE])
    await renderAt('/onaylarim')
    expect(await screen.findByRole('heading', { level: 1, name: APPROVALS_TEXT.heading })).toBeTruthy()
    expect(screen.getByText(APPROVALS_TEXT.pilotScope)).toBeTruthy()
    expect(screen.getByText(APPROVALS_TEXT.nativeOnly)).toBeTruthy()
    const table = await screen.findByTestId('work-approvals-table')
    const row = within(table).getAllByRole('row')[1]
    expect(row.getAttribute('data-approval-id')).toBe(APPROVAL)
    expect(within(row).getByText(APPROVALS_TEXT.kindFinanceOfficialInvoiceImport)).toBeTruthy()
    expect(within(row).getByText('maker')).toBeTruthy()
    expect(within(row).getByRole('link', { name: APPROVALS_TEXT.openNative }).getAttribute('href')).toBe(NATIVE_PATH)
    expect(within(row).getByRole('link', { name: APPROVALS_TEXT.openDetail }).getAttribute('href'))
      .toBe(`/onaylarim/finance.official-invoice-import.v1/${APPROVAL}`)
    // Read only: no approve control on this screen; one GET, under the committed company.
    expect(screen.queryByRole('button', { name: /onayla/i })).toBeNull()
    expect(work()).toEqual([{ url: '/api/v1/work/approvals', companyId: COMPANY, method: 'GET' }])
    expect(screen.getByText(APPROVALS_TEXT.end)).toBeTruthy()
  })

  it('opens the record detail and from there Finance\'s own approval screen', async () => {
    installFetch([APPROVE])
    const router = await renderAt('/onaylarim')
    const user = userEvent.setup()
    await user.click(await screen.findByRole('link', { name: APPROVALS_TEXT.openDetail }))
    const facts = await screen.findByTestId('work-approval-facts')
    expect(within(facts).getByText(APPROVALS_TEXT.statusPending)).toBeTruthy()
    expect(within(facts).getByText('maker')).toBeTruthy()
    expect(within(facts).getByText(item.templateVersion)).toBeTruthy()
    expect(screen.getByRole('link', { name: APPROVALS_TEXT.openNative }).getAttribute('href')).toBe(NATIVE_PATH)
    await user.click(screen.getByRole('link', { name: `← ${APPROVALS_TEXT.backToList}` }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/onaylarim'))
  })

  it('pages with the owner\'s cursor and never repeats a row', async () => {
    const second = { ...item, approvalId: '22222222-2222-4222-8222-222222222222' }
    const { work } = installFetch([APPROVE], {
      list: (url) => url.includes('cursor=')
        ? json({ companyId: COMPANY, items: [second, item], nextCursor: null })
        : json({ companyId: COMPANY, items: [item], nextCursor: 'c-1' }),
    })
    await renderAt('/onaylarim')
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await waitFor(() => expect(within(screen.getByTestId('work-approvals-table')).getAllByRole('row')).toHaveLength(3))
    expect(work().map((call) => call.url)).toEqual(['/api/v1/work/approvals', '/api/v1/work/approvals?cursor=c-1'])
  })

  describe('a next page that does not verify (#642 K2 P2)', () => {
    const SECOND = '22222222-2222-4222-8222-222222222222'
    const rowsShown = () => document.querySelectorAll('[data-testid="work-approvals-table"] tbody tr').length
    const nativeLinks = () => screen.queryAllByRole('link', { name: APPROVALS_TEXT.openNative })

    it.each([
      ['another company\'s page', () => json({ companyId: OTHER_COMPANY, items: [{ ...item, approvalId: SECOND, companyId: OTHER_COMPANY }], nextCursor: null })],
      ['a row of another state', () => json({ companyId: COMPANY, items: [{ ...item, approvalId: SECOND, status: 'Approved' }], nextCursor: null })],
      ['a broken body', () => new Response('not json', { status: 200 })],
      ['a 404', () => json({ status: 404 }, 404)],
      ['a 409', () => json({ status: 409 }, 409)],
    ])('%s closes the list: no earlier row, link or "rows are valid" text; recovery is a fresh first page', async (_, bad) => {
      let firstReads = 0
      const { work } = installFetch([APPROVE], {
        list: (url) => {
          if (url.includes('cursor=')) return bad()
          firstReads += 1
          return json({ companyId: COMPANY, items: [item], nextCursor: 'c-1' })
        },
      })
      await renderAt('/onaylarim')
      const user = userEvent.setup()
      await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
      const closed = await screen.findByTestId('work-approvals-closed')
      expect(closed.textContent).toContain(APPROVALS_TEXT.listClosed)
      expect(screen.queryByTestId('work-approvals-table')).toBeNull()
      expect(nativeLinks()).toHaveLength(0)
      expect(screen.queryByRole('link', { name: APPROVALS_TEXT.openDetail })).toBeNull()
      expect(screen.queryByText(APPROVALS_TEXT.nextPageFailed)).toBeNull()
      expect(screen.queryByRole('button', { name: APPROVALS_TEXT.loadMore })).toBeNull()

      await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.restart }))
      await waitFor(() => expect(rowsShown()).toBe(1))
      expect(firstReads).toBe(2)
      // The bad cursor was read once and never resent; the recovery read has no cursor, under the same company.
      expect(work().map((call) => call.url)).toEqual([
        '/api/v1/work/approvals', '/api/v1/work/approvals?cursor=c-1', '/api/v1/work/approvals',
      ])
      expect(work().every((call) => call.companyId === COMPANY)).toBe(true)
    })

    it.each([401, 403])('a %s on the next page leaves no earlier row or link on screen', async (status) => {
      installFetch([APPROVE], {
        list: (url) => url.includes('cursor=')
          ? json({ status }, status)
          : json({ companyId: COMPANY, items: [item], nextCursor: 'c-1' }),
      })
      await renderAt('/onaylarim')
      const user = userEvent.setup()
      await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
      await waitFor(() => expect(screen.queryByTestId('work-approvals-table')).toBeNull())
      expect(nativeLinks()).toHaveLength(0)
      expect(screen.queryByText(APPROVALS_TEXT.nextPageFailed)).toBeNull()
    })

    it.each([
      ['a 503', () => json({ status: 503 }, 503)],
      ['no response', () => { throw new TypeError('Failed to fetch') }],
    ])('%s only for now keeps the verified rows and retries the SAME cursor', async (_, transient) => {
      let nextReads = 0
      const { work } = installFetch([APPROVE], {
        list: (url) => {
          if (!url.includes('cursor=')) return json({ companyId: COMPANY, items: [item], nextCursor: 'c-1' })
          nextReads += 1
          return nextReads === 1 ? transient() : json({ companyId: COMPANY, items: [{ ...item, approvalId: SECOND }], nextCursor: null })
        },
      })
      await renderAt('/onaylarim')
      const user = userEvent.setup()
      await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
      expect(await screen.findByText(APPROVALS_TEXT.nextPageFailed)).toBeTruthy()
      expect(rowsShown()).toBe(1)
      expect(screen.queryByTestId('work-approvals-closed')).toBeNull()
      await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
      await waitFor(() => expect(rowsShown()).toBe(2))
      expect(work().map((call) => call.url)).toEqual([
        '/api/v1/work/approvals', '/api/v1/work/approvals?cursor=c-1', '/api/v1/work/approvals?cursor=c-1',
      ])
      expect(screen.getByText(APPROVALS_TEXT.end)).toBeTruthy()
    })
  })

  it.each([
    [403, 'forbidden'],
    [401, 'unauthenticated'],
  ])('a %s on the first page shows no row', async (status) => {
    installFetch([APPROVE], { list: () => json({ status }, status) })
    await renderAt('/onaylarim')
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByTestId('work-approvals-table')).toBeNull()
  })

  it('says plainly when nothing waits', async () => {
    installFetch([APPROVE], { list: () => json({ companyId: COMPANY, items: [], nextCursor: null }) })
    await renderAt('/onaylarim')
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(screen.queryByTestId('work-approvals-table')).toBeNull()
  })

  it('shows a refused or unverifiable read as a state, never as rows', async () => {
    installFetch([APPROVE], { list: () => json({ companyId: OTHER_COMPANY, items: [{ ...item, companyId: OTHER_COMPANY }], nextCursor: null }) })
    await renderAt('/onaylarim')
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByTestId('work-approvals-table')).toBeNull()
  })

  it('a 503 is the standard unavailable state with a retry', async () => {
    installFetch([APPROVE], { list: () => json({ status: 503 }, 503) })
    await renderAt('/onaylarim')
    await waitFor(() => expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
  })

  it('a detail of another approval than the one asked for is not shown', async () => {
    installFetch([APPROVE], { detail: () => json({ ...detail, item: { ...item, approvalId: '22222222-2222-4222-8222-222222222222' } }) })
    await renderAt(`/onaylarim/finance.official-invoice-import.v1/${APPROVAL}`)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByTestId('work-approval-facts')).toBeNull()
  })

  it.each(['/onaylarim', `/onaylarim/finance.official-invoice-import.v1/${APPROVAL}`])(
    'without the native capability %s is the neutral not-found block and reads nothing', async (path) => {
      const { work } = installFetch([CAPABILITY.financeOfficialInvoiceImportUpload])
      await renderAt(path)
      await waitFor(() => expect(notFound()).not.toBeNull())
      expect(screen.queryByRole('heading', { level: 1, name: APPROVALS_TEXT.heading })).toBeNull()
      expect(work()).toEqual([])
    })
})

/**
 * #645: Sales order drafts awaiting internal approval, next to (never mixed with) the Finance pilot. Each source is
 * listed only for its owner's own approve capability, read with its own kind, and opens its owner's own screen.
 */
describe('/onaylarim — Sales and Finance as separate connected sources', () => {
  const SALES_APPROVE = SALES_CAPABILITY.ordersApprove
  const SALES_DETAIL = SALES_CAPABILITY.draftsDetail
  const SALES_LIST = '/api/v1/work/approvals?kind=sales.order-draft.v1'
  const OTHER_DRAFT = '22222222-2222-4222-8222-222222222222'
  const bySource = (sales: () => Response, finance: () => Response = () => json({ companyId: COMPANY, items: [item], nextCursor: null })) =>
    (url: string) => url.includes('kind=sales.order-draft.v1') ? sales() : finance()
  const salesPage = (items: readonly unknown[] = [salesItem], nextCursor: string | null = null) => json({ companyId: COMPANY, items, nextCursor })

  it('a Sales-only approver gets only the Sales list, asked for by its kind, linking the Sales order screen', async () => {
    const { work } = installFetch([SALES_APPROVE, SALES_DETAIL], { list: bySource(() => salesPage()) })
    await renderAt('/onaylarim')
    const table = await screen.findByTestId('work-approvals-table')
    expect(table.getAttribute('data-kind')).toBe('sales.order-draft.v1')
    const row = within(table).getAllByRole('row')[1]
    expect(row.getAttribute('data-approval-id')).toBe(DRAFT)
    expect(within(row).getByText('SO-2026-000123')).toBeTruthy()
    expect(within(row).getByText('Rev.0')).toBeTruthy()
    expect(within(row).getByRole('link', { name: APPROVALS_TEXT.openNativeSales }).getAttribute('href')).toBe(`/satis/siparisler/${DRAFT}`)
    expect(within(row).getByRole('link', { name: APPROVALS_TEXT.openDetail }).getAttribute('href'))
      .toBe(`/onaylarim/sales.order-draft.v1/${DRAFT}`)
    // No source switch for one source; the Finance list is never read; no approve control here.
    expect(screen.queryByRole('navigation', { name: APPROVALS_TEXT.sources })).toBeNull()
    expect(work().map((call) => call.url)).toEqual([SALES_LIST])
    expect(work().every((call) => call.method === 'GET' && call.companyId === COMPANY)).toBe(true)
    expect(screen.queryByRole('button', { name: /onayla/i })).toBeNull()
  })

  it('shows isOwnDraft as a fact only, and draws no Sales link without the Sales order screen capability', async () => {
    installFetch([SALES_APPROVE], { list: bySource(() => salesPage([{ ...salesItem, isOwnDraft: true }])) })
    await renderAt('/onaylarim')
    const row = within(await screen.findByTestId('work-approvals-table')).getAllByRole('row')[1]
    expect(within(row).getByText(APPROVALS_TEXT.ownDraftYes)).toBeTruthy()
    expect(within(row).queryByRole('link', { name: APPROVALS_TEXT.openNativeSales })).toBeNull()
    expect(within(row).getByRole('link', { name: APPROVALS_TEXT.openDetail })).toBeTruthy()
  })

  it('an approver of both sees a source switch; each source is read with its own request and never mixed', async () => {
    const { work } = installFetch([APPROVE, SALES_APPROVE, SALES_DETAIL], { list: bySource(() => salesPage()) })
    const router = await renderAt('/onaylarim')
    const nav = await screen.findByRole('navigation', { name: APPROVALS_TEXT.sources })
    expect(within(nav).getAllByRole('link').map((link) => link.textContent)).toEqual(['Finans', 'Satış'])
    expect(within(nav).getByRole('link', { name: 'Finans' }).getAttribute('aria-current')).toBe('page')
    const finance = await screen.findByTestId('work-approvals-table')
    expect(finance.getAttribute('data-kind')).toBe('finance.official-invoice-import.v1')
    expect(within(finance).queryByText('SO-2026-000123')).toBeNull()

    const user = userEvent.setup()
    await user.click(within(nav).getByRole('link', { name: 'Satış' }))
    await waitFor(() => expect(screen.getByTestId('work-approvals-table').getAttribute('data-kind')).toBe('sales.order-draft.v1'))
    expect(router.state.location.search).toBe('?kaynak=satis')
    const sales = screen.getByTestId('work-approvals-table')
    expect(within(sales).queryByRole('link', { name: APPROVALS_TEXT.openNative })).toBeNull()
    expect(sales.querySelectorAll(`[data-approval-id="${APPROVAL}"]`)).toHaveLength(0)
    expect(work().map((call) => call.url)).toEqual(['/api/v1/work/approvals', SALES_LIST])
  })

  it('a source the actor does not hold cannot be selected by the address', async () => {
    const { work } = installFetch([APPROVE], { list: bySource(() => salesPage()) })
    await renderAt('/onaylarim?kaynak=satis')
    const table = await screen.findByTestId('work-approvals-table')
    expect(table.getAttribute('data-kind')).toBe('finance.official-invoice-import.v1')
    expect(work().some((call) => call.url.includes('kind=sales'))).toBe(false)
  })

  it('a source switch drops the other source\'s rows, cursor and continuation error', async () => {
    const { work } = installFetch([APPROVE, SALES_APPROVE, SALES_DETAIL], {
      list: (url) => {
        if (url.includes('kind=sales.order-draft.v1')) {
          return url.includes('cursor=') ? json({ status: 503 }, 503) : salesPage([salesItem], 's-1')
        }
        return json({ companyId: COMPANY, items: [item], nextCursor: 'f-1' })
      },
    })
    const router = await renderAt('/onaylarim?kaynak=satis')
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
    expect(await screen.findByText(APPROVALS_TEXT.nextPageFailed)).toBeTruthy()
    await user.click(within(screen.getByRole('navigation', { name: APPROVALS_TEXT.sources })).getByRole('link', { name: 'Finans' }))
    await waitFor(() => expect(screen.getByTestId('work-approvals-table').getAttribute('data-kind')).toBe('finance.official-invoice-import.v1'))
    expect(screen.queryByText(APPROVALS_TEXT.nextPageFailed)).toBeNull()
    expect(screen.queryByText('SO-2026-000123')).toBeNull()
    // The Finance list continues with ITS cursor, never the Sales one.
    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await waitFor(() => expect(work().some((call) => call.url === '/api/v1/work/approvals?cursor=f-1')).toBe(true))
    expect(work().filter((call) => call.url.includes('cursor=s-1'))).toHaveLength(1)
    expect(work().some((call) => call.url.includes('kind=sales') && call.url.includes('cursor=f-1'))).toBe(false)
    expect(router.state.location.search).toBe('?kaynak=finans')
  })

  it('a foreign-company Sales next page closes the Sales list; a fresh first page recovers it', async () => {
    const { work } = installFetch([SALES_APPROVE, SALES_DETAIL], {
      list: (url) => url.includes('cursor=')
        ? json({ companyId: OTHER_COMPANY, items: [{ ...salesItem, approvalId: OTHER_DRAFT, companyId: OTHER_COMPANY }], nextCursor: null })
        : salesPage([salesItem], 's-1'),
    })
    await renderAt('/onaylarim')
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await screen.findByTestId('work-approvals-closed')
    expect(screen.queryByTestId('work-approvals-table')).toBeNull()
    expect(screen.queryByRole('link', { name: APPROVALS_TEXT.openNativeSales })).toBeNull()
    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.restart }))
    await screen.findByTestId('work-approvals-table')
    expect(work().map((call) => call.url)).toEqual([SALES_LIST, `${SALES_LIST}&cursor=s-1`, SALES_LIST])
  })

  it('opens the Sales detail (item only) and from there the Sales order screen', async () => {
    const { work } = installFetch([SALES_APPROVE, SALES_DETAIL], { list: bySource(() => salesPage()), detail: () => json(salesDetail) })
    await renderAt(`/onaylarim/sales.order-draft.v1/${DRAFT}`)
    const facts = await screen.findByTestId('work-approval-facts')
    expect(screen.getByRole('heading', { level: 1, name: APPROVALS_TEXT.detailHeadingSales })).toBeTruthy()
    expect(within(facts).getByText('SO-2026-000123')).toBeTruthy()
    expect(within(facts).getByText('Rev.0')).toBeTruthy()
    expect(within(facts).getByText('3')).toBeTruthy()
    expect(screen.getByRole('link', { name: APPROVALS_TEXT.openNativeSales }).getAttribute('href')).toBe(`/satis/siparisler/${DRAFT}`)
    expect(screen.getByRole('link', { name: `← ${APPROVALS_TEXT.backToList}` }).getAttribute('href')).toBe('/onaylarim?kaynak=satis')
    expect(work().map((call) => call.url)).toEqual([`/api/v1/work/approvals/sales.order-draft.v1/${DRAFT}`])
  })

  it.each([
    ['a Finance-shaped answer', () => json(detail)],
    ['another draft', () => json({ item: { ...salesItem, approvalId: OTHER_DRAFT } })],
  ])('a Sales detail with %s is not shown', async (_, answer) => {
    installFetch([SALES_APPROVE, SALES_DETAIL], { detail: answer })
    await renderAt(`/onaylarim/sales.order-draft.v1/${DRAFT}`)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByTestId('work-approval-facts')).toBeNull()
  })

  it.each([
    ['a Sales detail for a Finance-only approver', [APPROVE], `/onaylarim/sales.order-draft.v1/${DRAFT}`],
    ['a Finance detail for a Sales-only approver', [SALES_APPROVE, SALES_DETAIL], `/onaylarim/finance.official-invoice-import.v1/${APPROVAL}`],
    ['an unconnected kind', [APPROVE, SALES_APPROVE], `/onaylarim/purchasing.order.v1/${DRAFT}`],
  ])('%s is the neutral not-found block and reads nothing', async (_, granted, path) => {
    const { work } = installFetch(granted)
    await renderAt(path)
    await waitFor(() => expect(notFound()).not.toBeNull())
    expect(screen.queryByTestId('work-approval-facts')).toBeNull()
    expect(work()).toEqual([])
  })

  it('the home entry names exactly the sources the actor holds', async () => {
    installFetch([SALES_APPROVE])
    await renderAt('/')
    const entry = await screen.findByTestId('home-approvals')
    expect(entry.textContent).toContain(APPROVALS_TEXT.homeText(['Satış']))
    expect(entry.textContent).not.toContain('Finans')
  })

  it('an actor with neither approve capability (even with the Sales read) gets no entry and no inbox', async () => {
    const { work } = installFetch([SALES_DETAIL, CAPABILITY.salesDraftsList, CAPABILITY.financeOfficialInvoiceImportUpload])
    await renderAt('/onaylarim')
    await waitFor(() => expect(notFound()).not.toBeNull())
    expect(work()).toEqual([])
  })
})

/**
 * #647 K2 P2 and the context-change rules: a source closed by a terminal next-page answer stays closed across a
 * source switch under the PRODUCTION cache timing (staleTime 30 s), until a fresh cursorless first page; a late answer
 * never paints into another source or another company.
 */
describe('/onaylarim — closure, late answers and context changes', () => {
  const SALES_APPROVE = SALES_CAPABILITY.ordersApprove
  const SALES_DETAIL = SALES_CAPABILITY.draftsDetail
  const SALES_LIST = '/api/v1/work/approvals?kind=sales.order-draft.v1'
  const BOTH = [APPROVE, SALES_APPROVE, SALES_DETAIL]
  const sourceLink = (name: string) => within(screen.getByRole('navigation', { name: APPROVALS_TEXT.sources })).getByRole('link', { name })
  const salesRows = () => document.querySelectorAll(`[data-approval-id="${DRAFT}"]`).length

  it('a Sales list closed by a foreign next page stays closed after Finance and back (30 s cache), until a fresh first page', async () => {
    const { work } = installFetch(BOTH, {
      list: (url) => {
        if (!url.includes('kind=sales.order-draft.v1')) return json({ companyId: COMPANY, items: [item], nextCursor: null })
        return url.includes('cursor=')
          ? json({ companyId: OTHER_COMPANY, items: [{ ...salesItem, approvalId: '22222222-2222-4222-8222-222222222222', companyId: OTHER_COMPANY }], nextCursor: null })
          : json({ companyId: COMPANY, items: [salesItem], nextCursor: 's-1' })
      },
    })
    await renderAt('/onaylarim?kaynak=satis', 30_000)
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await screen.findByTestId('work-approvals-closed')

    await user.click(sourceLink('Finans'))
    await waitFor(() => expect(screen.getByTestId('work-approvals-table').getAttribute('data-kind')).toBe('finance.official-invoice-import.v1'))
    await user.click(sourceLink('Satış'))
    // Back on Sales: still closed — no cached row, detail link or Sales link, and nothing read.
    expect(await screen.findByTestId('work-approvals-closed')).toBeTruthy()
    expect(screen.queryByTestId('work-approvals-table')).toBeNull()
    expect(salesRows()).toBe(0)
    expect(screen.queryByRole('link', { name: APPROVALS_TEXT.openNativeSales })).toBeNull()
    expect(screen.queryByRole('link', { name: APPROVALS_TEXT.openDetail })).toBeNull()
    const salesReads = () => work().filter((call) => call.url.startsWith(SALES_LIST))
    expect(salesReads().map((call) => call.url)).toEqual([SALES_LIST, `${SALES_LIST}&cursor=s-1`])

    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.restart }))
    await waitFor(() => expect(salesRows()).toBe(1))
    expect(salesReads().map((call) => call.url)).toEqual([SALES_LIST, `${SALES_LIST}&cursor=s-1`, SALES_LIST])
    // And after the restart a switch away and back does not resurrect the closed page or the pre-closure page.
    await user.click(sourceLink('Finans'))
    await user.click(sourceLink('Satış'))
    await waitFor(() => expect(salesRows()).toBe(1))
    expect(screen.queryByTestId('work-approvals-closed')).toBeNull()
  })

  it('a Sales first page that answers after the switch to Finance never appears', async () => {
    const late = deferredResponse()
    installFetch(BOTH, {
      list: (url) => url.includes('kind=sales.order-draft.v1') ? late.response : json({ companyId: COMPANY, items: [item], nextCursor: null }),
    })
    await renderAt('/onaylarim?kaynak=satis')
    await waitFor(() => expect(late.requested()).toBe(true))
    const user = userEvent.setup()
    await user.click(sourceLink('Finans'))
    await waitFor(() => expect(screen.getByTestId('work-approvals-table').getAttribute('data-kind')).toBe('finance.official-invoice-import.v1'))
    late.resolve(json({ companyId: COMPANY, items: [salesItem], nextCursor: null }))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(screen.getByTestId('work-approvals-table').getAttribute('data-kind')).toBe('finance.official-invoice-import.v1')
    expect(salesRows()).toBe(0)
  })

  it('a company switch re-reads under the new company; the old company\'s rows and late next page never show', async () => {
    const lateNext = deferredResponse()
    const { work } = installFetch([SALES_APPROVE, SALES_DETAIL], {
      list: (url, companyId) => {
        if (companyId === OTHER_COMPANY) return json({ companyId: OTHER_COMPANY, items: [], nextCursor: null })
        return url.includes('cursor=') ? lateNext.response : json({ companyId: COMPANY, items: [salesItem], nextCursor: 's-1' })
      },
    })
    await renderAt('/onaylarim')
    const user = userEvent.setup()
    await waitFor(() => expect(salesRows()).toBe(1))
    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await waitFor(() => expect(lateNext.requested()).toBe(true))
    await user.selectOptions(screen.getByLabelText('Şirket'), OTHER_COMPANY)
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(salesRows()).toBe(0)
    lateNext.resolve(json({ companyId: COMPANY, items: [{ ...salesItem, approvalId: '22222222-2222-4222-8222-222222222222' }], nextCursor: null }))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(salesRows()).toBe(0)
    expect(document.querySelectorAll('[data-approval-id="22222222-2222-4222-8222-222222222222"]')).toHaveLength(0)
    const companies = work().map((call) => call.companyId)
    expect(companies.slice(-1)).toEqual([OTHER_COMPANY])
    expect(work().filter((call) => call.companyId === OTHER_COMPANY).every((call) => call.url === SALES_LIST)).toBe(true)
  })
})

/** A list answer the test releases by hand, recording whether it was asked for. */
function deferredResponse() {
  let release!: (response: Response) => void
  let asked = false
  const promise = new Promise<Response>((resolve) => { release = resolve })
  return {
    get response() { asked = true; return promise as unknown as Response },
    requested: () => asked,
    resolve: (response: Response) => release(response),
  }
}

/**
 * #647 K2 delta P2: an old Sales instance's continuation answers terminally only AFTER the user left, came back, had
 * the new instance closed and recovered it. That late answer must neither close the recovered list nor roll its read
 * generation back (which would let a later restart reuse a cached first page instead of a fresh cursorless GET).
 */
describe('/onaylarim — a late terminal answer of a left source instance', () => {
  it('cannot close the recovered list or roll back its generation (30 s cache)', async () => {
    const SALES_LIST = '/api/v1/work/approvals?kind=sales.order-draft.v1'
    const FOREIGN = json({ companyId: OTHER_COMPANY, items: [{ ...salesItem, approvalId: '22222222-2222-4222-8222-222222222222', companyId: OTHER_COMPANY }], nextCursor: null })
    let release!: (response: Response) => void
    const oldContinuation = new Promise<Response>((resolve) => { release = resolve })
    let continuations = 0
    const { work } = installFetch([APPROVE, SALES_CAPABILITY.ordersApprove, SALES_CAPABILITY.draftsDetail], {
      list: (url) => {
        if (!url.includes('kind=sales.order-draft.v1')) return json({ companyId: COMPANY, items: [item], nextCursor: null })
        if (!url.includes('cursor=')) return json({ companyId: COMPANY, items: [salesItem], nextCursor: 's-1' })
        continuations += 1
        // R0 (the first continuation, of the instance the user leaves) stays pending; later ones are terminal at once.
        return continuations === 1 ? oldContinuation as unknown as Response : FOREIGN.clone()
      },
    })
    await renderAt('/onaylarim?kaynak=satis', 30_000)
    const user = userEvent.setup()
    const sourceLink = (name: string) => within(screen.getByRole('navigation', { name: APPROVALS_TEXT.sources })).getByRole('link', { name })
    const salesRows = () => document.querySelectorAll(`[data-approval-id="${DRAFT}"]`).length
    const cursorlessSalesReads = () => work().filter((call) => call.url === SALES_LIST).length

    // R0 starts in the first Sales instance, then the user leaves and comes back.
    await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await waitFor(() => expect(continuations).toBe(1))
    await user.click(sourceLink('Finans'))
    await user.click(sourceLink('Satış'))
    // The new instance: its own continuation is terminal → closed → restart → fresh cursorless first page.
    await user.click(await screen.findByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await screen.findByTestId('work-approvals-closed')
    const beforeRestart = cursorlessSalesReads()
    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.restart }))
    await waitFor(() => expect(salesRows()).toBe(1))
    expect(cursorlessSalesReads()).toBe(beforeRestart + 1)

    // Now the old R0 answers terminally: nothing changes.
    release(FOREIGN.clone())
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(screen.queryByTestId('work-approvals-closed')).toBeNull()
    expect(salesRows()).toBe(1)

    // The generation was not rolled back: the next closure's restart still reads a fresh cursorless first page.
    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await screen.findByTestId('work-approvals-closed')
    const beforeSecondRestart = cursorlessSalesReads()
    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.restart }))
    await waitFor(() => expect(salesRows()).toBe(1))
    expect(cursorlessSalesReads()).toBe(beforeSecondRestart + 1)
  })
})

/**
 * #667: Inventory stock-count differences and stock scraps as two more connected sources. Each is listed only for its
 * own native approve capability, read by its own kind, and opens the label's own screen — where the count/scrap panel
 * decides. Work stays read-only.
 */
describe('/onaylarim — Inventory stock-count and stock-scrap sources (#667)', () => {
  const COUNT_APPROVE = INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY
  const SCRAP_APPROVE = INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY
  const LABEL_SCREEN = INVENTORY_STOCK_POSITIONS_CAPABILITY
  const COUNT_LIST = '/api/v1/work/approvals?kind=inventory.stock-count.v1'
  const SCRAP_LIST = '/api/v1/work/approvals?kind=inventory.stock-scrap.v1'
  const OTHER_COUNT = 'cccccccc-2222-4222-8222-000000000667'
  const OPEN_LABEL = 'Etiket ekranında aç'
  const page = (items: readonly unknown[], nextCursor: string | null = null, companyId = COMPANY) => json({ companyId, items, nextCursor })
  /** One answer per requested kind; a kind the test did not serve is a 503, never another kind's rows. */
  const byKind = (served: Partial<Record<'count' | 'scrap' | 'sales' | 'finance', (url: string, companyId: string | null) => Response>>) =>
    (url: string, companyId: string | null) => {
      const answer = url.includes('kind=inventory.stock-count.v1') ? served.count
        : url.includes('kind=inventory.stock-scrap.v1') ? served.scrap
          : url.includes('kind=sales.order-draft.v1') ? served.sales
            : served.finance
      return answer === undefined ? json({ code: 'unavailable' }, 503) : answer(url, companyId)
    }
  const table = () => screen.getByTestId('work-approvals-table')
  const bodyRows = () => within(table()).getAllByRole('row').slice(1)
  const cells = (row: HTMLElement) => within(row).getAllByRole('cell').map((cell) => cell.textContent)
  const headers = () => within(table()).getAllByRole('columnheader').map((header) => header.textContent)
  const facts = () => within(screen.getByTestId('work-approval-facts')).getAllByRole('term')
    .map((term) => [term.textContent, term.nextElementSibling?.textContent])

  it('a stock-count approver gets only the count list, asked for by its kind, with the producer\'s exact facts', async () => {
    const { work } = installFetch([COUNT_APPROVE, LABEL_SCREEN], { list: byKind({ count: () => page([countItem]) }) })
    await renderAt('/onaylarim')
    await screen.findByTestId('work-approvals-table')
    expect(table().getAttribute('data-kind')).toBe('inventory.stock-count.v1')
    expect(headers()).toEqual(['Etiket kimliği', 'Defter (kg)', 'Sayılan (kg)', 'Fark (kg)', 'Gerekçe', 'Gönderen', 'Gönderim', 'Kayıt'])
    const [row] = bodyRows()
    expect(bodyRows()).toHaveLength(1)
    expect(row.getAttribute('data-approval-id')).toBe(COUNT)
    expect(row.getAttribute('data-label-id')).toBe(LABEL)
    // Kilograms are the producer's own text in Turkish digits; nothing is rounded, summed or re-derived.
    expect(cells(row).slice(0, 6)).toEqual([
      LABEL, '1.250,5', '1.247,25', '-3,25', 'Sayımda rulo ucu eksik çıktı; tartı fişi ekte.', 'stok-sayici',
    ])
    expect(within(row).getByRole('link', { name: OPEN_LABEL }).getAttribute('href')).toBe(`/stok/etiket/${LABEL}`)
    expect(within(row).getByRole('link', { name: APPROVALS_TEXT.openDetail }).getAttribute('href'))
      .toBe(`/onaylarim/inventory.stock-count.v1/${COUNT}`)
    // One source: no source switch; no other source is read; Work sends nothing but reads and has no decision control.
    expect(screen.queryByRole('navigation', { name: APPROVALS_TEXT.sources })).toBeNull()
    expect(work().map((call) => call.url)).toEqual([COUNT_LIST])
    expect(work().every((call) => call.method === 'GET' && call.companyId === COMPANY)).toBe(true)
    expect(screen.queryByRole('button', { name: /onayla|reddet/i })).toBeNull()
    expect(screen.getByText(APPROVALS_TEXT.pilotScope)).toBeTruthy()
    expect(APPROVALS_TEXT.pilotScope).toContain('Stok — sayım farkı ve hurda çıkışı onayları')
  })

  it('a stock-scrap approver gets only the scrap list; a surplus is signed too', async () => {
    const { work } = installFetch([SCRAP_APPROVE, LABEL_SCREEN], { list: byKind({ scrap: () => page([scrapItem]) }) })
    await renderAt('/onaylarim')
    await screen.findByTestId('work-approvals-table')
    expect(table().getAttribute('data-kind')).toBe('inventory.stock-scrap.v1')
    expect(headers()).toEqual(['Etiket kimliği', 'Defter (kg)', 'Hurda (kg)', 'Kalan (kg)', 'Gerekçe', 'Gönderen', 'Gönderim', 'Kayıt'])
    const [row] = bodyRows()
    expect(row.getAttribute('data-approval-id')).toBe(SCRAP)
    expect(cells(row).slice(0, 6)).toEqual([
      SCRAP_LABEL, '100', '6', '94', 'Kenar ezilmesi; müşteri onayıyla hurdaya ayrıldı.', 'hurda-kaydeden',
    ])
    expect(within(row).getByRole('link', { name: OPEN_LABEL }).getAttribute('href')).toBe(`/stok/etiket/${SCRAP_LABEL}`)
    expect(work().map((call) => call.url)).toEqual([SCRAP_LIST])
    expect(formatSignedKg('0.5')).toBe('+0,5')
    expect(formatSignedKg('-1250.5')).toBe('-1.250,5')
  })

  it('draws no label link without the label screen\'s own capability; the detail link stays', async () => {
    installFetch([COUNT_APPROVE], { list: byKind({ count: () => page([countItem]) }) })
    await renderAt('/onaylarim')
    await screen.findByTestId('work-approvals-table')
    const [row] = bodyRows()
    expect(within(row).queryByRole('link', { name: OPEN_LABEL })).toBeNull()
    expect(document.querySelector('a[href^="/stok/etiket"]')).toBeNull()
    expect(within(row).getByRole('link', { name: APPROVALS_TEXT.openDetail })).toBeTruthy()
  })

  it('an approver of all four sees them named and separated; each source reads its own kind and shows each record once', async () => {
    const { work } = installFetch([APPROVE, SALES_CAPABILITY.ordersApprove, COUNT_APPROVE, SCRAP_APPROVE, LABEL_SCREEN], {
      list: byKind({
        finance: () => page([item]), sales: () => page([salesItem]), count: () => page([countItem]), scrap: () => page([scrapItem]),
      }),
    })
    const router = await renderAt('/onaylarim')
    const nav = await screen.findByRole('navigation', { name: APPROVALS_TEXT.sources })
    expect(within(nav).getAllByRole('link').map((link) => link.textContent)).toEqual(['Finans', 'Satış', 'Stok sayım farkı', 'Stok hurda çıkışı'])
    await screen.findByTestId('work-approvals-table')
    expect(table().getAttribute('data-kind')).toBe('finance.official-invoice-import.v1')

    const user = userEvent.setup()
    await user.click(within(nav).getByRole('link', { name: 'Stok sayım farkı' }))
    await waitFor(() => expect(table().getAttribute('data-kind')).toBe('inventory.stock-count.v1'))
    expect(router.state.location.search).toBe('?kaynak=stok-sayim')
    expect(within(nav).getByRole('link', { name: 'Stok sayım farkı' }).getAttribute('aria-current')).toBe('page')
    expect(document.querySelectorAll(`[data-approval-id="${COUNT}"]`)).toHaveLength(1)
    expect(document.querySelectorAll(`[data-approval-id="${APPROVAL}"], [data-approval-id="${SCRAP}"], [data-approval-id="${DRAFT}"]`)).toHaveLength(0)

    await user.click(within(nav).getByRole('link', { name: 'Stok hurda çıkışı' }))
    await waitFor(() => expect(table().getAttribute('data-kind')).toBe('inventory.stock-scrap.v1'))
    expect(router.state.location.search).toBe('?kaynak=stok-hurda')
    expect(document.querySelectorAll(`[data-approval-id="${SCRAP}"]`)).toHaveLength(1)
    expect(document.querySelectorAll(`[data-approval-id="${COUNT}"]`)).toHaveLength(0)
    // Finance keeps its kind-less default request; every other source is asked for by its exact kind.
    expect(work().map((call) => call.url)).toEqual(['/api/v1/work/approvals', COUNT_LIST, SCRAP_LIST])
    expect(work().every((call) => call.method === 'GET')).toBe(true)
  })

  it('a source the actor does not hold cannot be selected by the address, and is never read', async () => {
    const { work } = installFetch([COUNT_APPROVE, LABEL_SCREEN], { list: byKind({ count: () => page([countItem]), scrap: () => page([scrapItem]) }) })
    await renderAt('/onaylarim?kaynak=stok-hurda')
    await screen.findByTestId('work-approvals-table')
    expect(table().getAttribute('data-kind')).toBe('inventory.stock-count.v1')
    expect(document.querySelector(`[data-approval-id="${SCRAP}"]`)).toBeNull()
    expect(work().map((call) => call.url)).toEqual([COUNT_LIST])
  })

  it.each([
    ['a scrap-shaped answer', () => page([scrapItem])],
    ['an item of another company', () => page([{ ...countItem, companyId: OTHER_COMPANY }])],
    ['a page of another company', () => page([{ ...countItem, companyId: OTHER_COMPANY }], null, OTHER_COMPANY)],
    ['the same record twice', () => page([countItem, countItem])],
    ['an unknown member', () => page([{ ...countItem, labelCode: 'ROLL-1' }])],
    ['a difference that is not counted − book', () => page([{ ...countItem, differenceKg: '-3' }])],
    ['a decided record', () => page([{ ...countItem, status: 'Approved' }])],
  ])('a count list carrying %s is refused whole: a state, never rows', async (_, answer) => {
    installFetch([COUNT_APPROVE, LABEL_SCREEN], { list: byKind({ count: answer }) })
    await renderAt('/onaylarim')
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByTestId('work-approvals-table')).toBeNull()
    expect(document.querySelector('a[href^="/stok/etiket"]')).toBeNull()
  })

  it('pages with the owner\'s cursor; a transient next-page failure keeps the verified rows and retries the same cursor', async () => {
    let attempt = 0
    const { work } = installFetch([COUNT_APPROVE, LABEL_SCREEN], {
      list: byKind({
        count: (url) => {
          if (!url.includes('cursor=')) return page([countItem], 'c-1')
          attempt += 1
          return attempt === 1 ? json({ code: 'unavailable' }, 503) : page([{ ...countItem, approvalId: OTHER_COUNT }])
        },
      }),
    })
    await renderAt('/onaylarim')
    await screen.findByTestId('work-approvals-table')
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await screen.findByText(APPROVALS_TEXT.nextPageFailed)
    expect(bodyRows()).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: /yeniden dene|tekrar dene/i }))
    await waitFor(() => expect(bodyRows()).toHaveLength(2))
    expect(screen.getByText(APPROVALS_TEXT.end)).toBeTruthy()
    const next = work().filter((call) => call.url.includes('cursor='))
    expect(next.map((call) => call.url)).toEqual([`${COUNT_LIST}&cursor=c-1`, `${COUNT_LIST}&cursor=c-1`])
  })

  it('opens the count detail with every fact labelled, and from there the label\'s own screen', async () => {
    const { work } = installFetch([COUNT_APPROVE, LABEL_SCREEN], { detail: () => json(countDetail) })
    await renderAt(`/onaylarim/inventory.stock-count.v1/${COUNT}`)
    await screen.findByTestId('work-approval-facts')
    expect(screen.getByRole('heading', { level: 1, name: 'Stok sayım farkı onayı' })).toBeTruthy()
    expect(screen.getByTestId('work-approval-facts').getAttribute('data-kind')).toBe('inventory.stock-count.v1')
    expect(facts().slice(0, 12)).toEqual([
      ['Onay türü', 'Stok · Sayım farkı onayı'],
      ['Etiket kimliği', LABEL],
      ['Durum', 'Onay bekliyor'],
      ['Defter (kg)', '1.250,5'],
      ['Sayılan (kg)', '1.247,25'],
      ['Fark (kg)', '-3,25'],
      ['Gerekçe', 'Sayımda rulo ucu eksik çıktı; tartı fişi ekte.'],
      ['Blokaj', 'Serbest'],
      ['Mal sahibi', 'Şirket malı'],
      ['Malı tutan', 'Şirket'],
      ['Etiket sürümü', '4'],
      ['Gönderen', 'stok-sayici'],
    ])
    expect(screen.getByRole('link', { name: OPEN_LABEL }).getAttribute('href')).toBe(`/stok/etiket/${LABEL}`)
    expect(screen.getByRole('link', { name: `← ${APPROVALS_TEXT.backToList}` }).getAttribute('href')).toBe('/onaylarim?kaynak=stok-sayim')
    expect(work().map((call) => call.url)).toEqual([`/api/v1/work/approvals/inventory.stock-count.v1/${COUNT}`])
    expect(work().every((call) => call.method === 'GET')).toBe(true)
    expect(screen.queryByRole('button', { name: /onayla|reddet/i })).toBeNull()
  })

  it('opens the scrap detail: a Cari owner, a company holder and a blocked position in the stock screen\'s own words', async () => {
    installFetch([SCRAP_APPROVE, LABEL_SCREEN], { detail: () => json(scrapDetail) })
    await renderAt(`/onaylarim/inventory.stock-scrap.v1/${SCRAP}`)
    await screen.findByTestId('work-approval-facts')
    expect(screen.getByRole('heading', { level: 1, name: 'Stok hurda çıkışı onayı' })).toBeTruthy()
    expect(Object.fromEntries(facts())).toMatchObject({
      'Onay türü': 'Stok · Hurda çıkışı onayı',
      'Etiket kimliği': SCRAP_LABEL,
      'Defter (kg)': '100',
      'Hurda (kg)': '6',
      'Kalan (kg)': '94',
      'Blokaj': 'Bloklu',
      'Mal sahibi': 'Cari malı',
      'Malı tutan': 'Şirket',
      'Etiket sürümü': '7',
    })
    // The Cari owner is named by its kind only: the producer publishes no name, and no id is drawn in its place.
    expect(screen.getByTestId('work-approval-facts').textContent).not.toContain(COUNTERPARTY)
    expect(screen.getByRole('link', { name: OPEN_LABEL }).getAttribute('href')).toBe(`/stok/etiket/${SCRAP_LABEL}`)
    expect(screen.getByRole('link', { name: `← ${APPROVALS_TEXT.backToList}` }).getAttribute('href')).toBe('/onaylarim?kaynak=stok-hurda')
  })

  it.each([
    ['a scrap-shaped answer', () => json(scrapDetail)],
    ['another count', () => json({ item: { ...countItem, approvalId: OTHER_COUNT } })],
    ['another company\'s count', () => json({ item: { ...countItem, companyId: OTHER_COMPANY } })],
    ['an item with an unknown member', () => json({ item: { ...countItem, labelCode: 'ROLL-1' } })],
    ['a Finance-shaped answer', () => json(detail)],
    ['more than { item }', () => json({ ...countDetail, history: [] })],
  ])('a count detail with %s is not shown and offers no label link', async (_, answer) => {
    installFetch([COUNT_APPROVE, LABEL_SCREEN], { detail: answer })
    await renderAt(`/onaylarim/inventory.stock-count.v1/${COUNT}`)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByTestId('work-approval-facts')).toBeNull()
    expect(document.querySelector('a[href^="/stok/etiket"]')).toBeNull()
  })

  it.each([
    ['a count detail for a scrap-only approver', [SCRAP_APPROVE, LABEL_SCREEN], `/onaylarim/inventory.stock-count.v1/${COUNT}`],
    ['a scrap detail for a count-only approver', [COUNT_APPROVE, LABEL_SCREEN], `/onaylarim/inventory.stock-scrap.v1/${SCRAP}`],
    ['a count detail for a Finance-only approver', [APPROVE], `/onaylarim/inventory.stock-count.v1/${COUNT}`],
    ['an unconnected Inventory kind', [COUNT_APPROVE, SCRAP_APPROVE, LABEL_SCREEN], `/onaylarim/inventory.transfer.v1/${COUNT}`],
  ])('%s is the neutral not-found block and reads nothing', async (_, granted, path) => {
    const { work } = installFetch(granted)
    await renderAt(path)
    await waitFor(() => expect(notFound()).not.toBeNull())
    expect(screen.queryByTestId('work-approval-facts')).toBeNull()
    expect(work()).toEqual([])
  })

  it('an actor with the stock read and record names but neither approve capability gets no entry and no inbox', async () => {
    const { work } = installFetch([LABEL_SCREEN, INVENTORY_STOCK_COUNT_RECORD_CAPABILITY, INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY])
    await renderAt('/onaylarim')
    await waitFor(() => expect(notFound()).not.toBeNull())
    expect(work()).toEqual([])
  })

  it('the home entry names exactly the held sources, as one Turkish list', async () => {
    installFetch([APPROVE, COUNT_APPROVE, SCRAP_APPROVE])
    await renderAt('/')
    const entry = await screen.findByTestId('home-approvals')
    expect(entry.textContent).toContain('Onayınızı bekleyen kayıtlar: Finans, Stok sayım farkı ve Stok hurda çıkışı.')
    expect(entry.textContent).not.toContain('Satış')
    expect(APPROVALS_TEXT.homeText(['Finans', 'Satış'])).toBe('Onayınızı bekleyen kayıtlar: Finans ve Satış.')
    expect(APPROVALS_TEXT.homeText(['Stok sayım farkı'])).toBe('Onayınızı bekleyen kayıtlar: Stok sayım farkı.')
  })

  it('a count first page that answers after the switch to the scrap source never appears there', async () => {
    const late = deferredResponse()
    installFetch([COUNT_APPROVE, SCRAP_APPROVE, LABEL_SCREEN], {
      list: byKind({ count: () => late.response, scrap: () => page([scrapItem]) }),
    })
    await renderAt('/onaylarim')
    await waitFor(() => expect(late.requested()).toBe(true))
    const user = userEvent.setup()
    await user.click(within(await screen.findByRole('navigation', { name: APPROVALS_TEXT.sources })).getByRole('link', { name: 'Stok hurda çıkışı' }))
    await waitFor(() => expect(screen.getByTestId('work-approvals-table').getAttribute('data-kind')).toBe('inventory.stock-scrap.v1'))
    late.resolve(page([countItem]))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(table().getAttribute('data-kind')).toBe('inventory.stock-scrap.v1')
    expect(document.querySelectorAll(`[data-approval-id="${COUNT}"]`)).toHaveLength(0)
    expect(document.querySelectorAll(`[data-approval-id="${SCRAP}"]`)).toHaveLength(1)
  })

  it('a company switch re-reads under the new company; the old company\'s count rows and late next page never show', async () => {
    const lateNext = deferredResponse()
    const { work } = installFetch([COUNT_APPROVE, LABEL_SCREEN], {
      list: byKind({
        count: (url, companyId) => {
          if (companyId === OTHER_COMPANY) return page([], null, OTHER_COMPANY)
          return url.includes('cursor=') ? lateNext.response : page([countItem], 'c-1')
        },
      }),
    })
    await renderAt('/onaylarim')
    const user = userEvent.setup()
    await screen.findByTestId('work-approvals-table')
    await user.click(screen.getByRole('button', { name: APPROVALS_TEXT.loadMore }))
    await waitFor(() => expect(lateNext.requested()).toBe(true))
    await user.selectOptions(screen.getByLabelText('Şirket'), OTHER_COMPANY)
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(document.querySelectorAll('[data-approval-id]')).toHaveLength(0)
    lateNext.resolve(page([{ ...countItem, approvalId: OTHER_COUNT }]))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(document.querySelectorAll('[data-approval-id]')).toHaveLength(0)
    expect(document.querySelector('a[href^="/stok/etiket"]')).toBeNull()
    expect(work().map((call) => call.companyId).slice(-1)).toEqual([OTHER_COMPANY])
    expect(work().filter((call) => call.companyId === OTHER_COMPANY).every((call) => call.url === COUNT_LIST)).toBe(true)
  })
})

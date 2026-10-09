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
import { APPROVAL, COMPANY, detail, item } from './approvalInboxFixtures'
import { APPROVALS_TEXT } from './workText'

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

async function renderAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } })
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

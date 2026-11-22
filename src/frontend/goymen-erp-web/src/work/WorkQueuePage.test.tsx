/**
 * B-25 Work queue in the REAL shell (router, CapabilityRoute, ShellProvider, auth boundary) over a stubbed fetch that
 * plays `GET /api/v1/work/queue`. Component evidence only: each source's authority and the company fence are proven by
 * the server's own tests (WorkQueueEndpointTests, NativeApprovalQueueReadQueryV1Tests). Synthetic data.
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
import { COMPANY } from './approvalInboxFixtures'
import { APPROVALS_TEXT, FINANCE_SOURCE, WORK_QUEUE_TEXT } from './workText'
import { WORK_INBOX_TEXT } from './workInboxText'

const USER = '11111111-1111-1111-1111-111111111111'
const OTHER_COMPANY = '10000000-0000-0000-0000-000000000002'
const DIRECTORY = [
  { id: COMPANY, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false },
  { id: OTHER_COMPANY, code: 'GSP', legalName: 'Göymen B', isPlaceholder: false },
]
const APPROVE = CAPABILITY.financeOfficialInvoiceImportApprove
const FINANCE_KIND = 'finance.official-invoice-import.v1'
const PURCHASING_KIND = 'purchasing.request.v1'
const FIRST = 'c6000000-0000-4000-8000-000000000001'
const SECOND = 'c6000000-0000-4000-8000-000000000002'

const financeItem = {
  module: 'finance', itemId: FIRST, kind: FINANCE_KIND, createdAtUtc: '2026-11-01T07:00:00+00:00',
  link: `/onaylarim/${FINANCE_KIND}/${FIRST}`,
}
const purchasingItem = {
  module: 'purchasing', itemId: SECOND, kind: PURCHASING_KIND, createdAtUtc: '2026-11-02T07:00:00+00:00',
  link: `/satin-alma/talepler/${SECOND}`,
}
const ready = (module: string, kind: string, hasMore = false) => ({ module, kind, state: 'Ready', hasMore })
const queue = (overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY,
  items: [financeItem, purchasingItem],
  sources: [ready('finance', FINANCE_KIND), ready('purchasing', PURCHASING_KIND)],
  ...overrides,
})

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function installFetch(granted: readonly string[], served: () => Response | Promise<Response> = () => json(queue())) {
  const calls: { url: string; companyId: string | null }[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const headers = (init?.headers ?? {}) as Record<string, string>
    calls.push({ url, companyId: headers['X-Company-Id'] ?? null })
    if (url.includes('/identity/me')) return json({ userId: USER, displayName: 'Onaycı', email: null, allowedCompanyIds: [COMPANY, OTHER_COMPANY], mustChangePassword: false, localCredentialVersion: null, authenticationSessionBinding: null })
    if (url.includes('/organization/companies')) return json(DIRECTORY)
    if (url.includes('/shell/capabilities')) {
      return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'gen-1', capabilities: granted })
    }
    if (url.includes('/work/queue')) return served()
    if (url.includes('/work/approvals')) return json({ companyId: COMPANY, items: [], nextCursor: null })
    return json({ code: 'unavailable' }, 503)
  }))
  return { queueCalls: () => calls.filter((call) => call.url.includes('/work/queue')) }
}

async function renderAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
  await screen.findByText('Onaycı', undefined, { timeout: 5000 })
  return router
}

const stateBlock = (kind: string) => document.querySelector(`[data-state="${kind}"]`)

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

describe('/onaylarim/kuyruk — the Work queue', () => {
  it('lists every readable source in one list, oldest first, each row linking to its owner\'s screen', async () => {
    const { queueCalls } = installFetch([APPROVE])
    await renderAt('/onaylarim/kuyruk')
    expect(await screen.findByRole('heading', { level: 1, name: WORK_INBOX_TEXT.heading })).toBeTruthy()
    expect(screen.getByText(WORK_QUEUE_TEXT.scope)).toBeTruthy()
    const table = await screen.findByTestId('work-queue-table')
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => row.getAttribute('data-kind'))).toEqual([FINANCE_KIND, PURCHASING_KIND])
    expect(within(rows[0]).getByText(FINANCE_SOURCE.kindText)).toBeTruthy()
    expect(within(rows[0]).getByText('Finans')).toBeTruthy()
    expect(within(rows[0]).getByRole('link').getAttribute('href')).toBe(`/onaylarim/${FINANCE_KIND}/${FIRST}`)
    // A source the Work client has no words for is shown by its own names, never dropped.
    expect(within(rows[1]).getByText(PURCHASING_KIND)).toBeTruthy()
    expect(within(rows[1]).getByText('Satın alma')).toBeTruthy()
    expect(within(rows[1]).getByRole('link').getAttribute('href')).toBe(`/satin-alma/talepler/${SECOND}`)
    // Read only: no approve control; one GET under the committed company.
    expect(screen.queryByRole('button', { name: /onayla/i })).toBeNull()
    expect(queueCalls()).toEqual([{ url: '/api/v1/work/queue', companyId: COMPANY }])
    expect(screen.queryByTestId('work-queue-unavailable')).toBeNull()
    expect(screen.queryByTestId('work-queue-has-more')).toBeNull()
  })

  it('names an unavailable source and a source with more records, and still lists the others', async () => {
    installFetch([APPROVE], () => json(queue({
      items: [financeItem],
      sources: [ready('finance', FINANCE_KIND, true), { module: 'purchasing', kind: PURCHASING_KIND, state: 'Unavailable', hasMore: false }],
    })))
    await renderAt('/onaylarim/kuyruk')
    expect(await screen.findByTestId('work-queue-unavailable')).toBeTruthy()
    expect(screen.getByTestId('work-queue-unavailable').textContent).toBe(WORK_QUEUE_TEXT.unavailable([PURCHASING_KIND]))
    expect(screen.getByTestId('work-queue-has-more').textContent).toBe(WORK_QUEUE_TEXT.hasMore([FINANCE_SOURCE.kindText]))
    expect(within(screen.getByTestId('work-queue-table')).getAllByRole('row')).toHaveLength(2)
  })

  it('an empty queue is the no-data state; empty with an unreadable source does not claim nothing waits', async () => {
    installFetch([APPROVE], () => json(queue({ items: [], sources: [ready('finance', FINANCE_KIND)] })))
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(stateBlock('empty-no-data')).not.toBeNull())
    expect(screen.queryByTestId('work-queue-table')).toBeNull()
  })

  it('empty with an unreadable source shows the readable-empty line, not the no-data state', async () => {
    installFetch([APPROVE], () => json(queue({
      items: [],
      sources: [ready('finance', FINANCE_KIND), { module: 'purchasing', kind: PURCHASING_KIND, state: 'Unavailable', hasMore: false }],
    })))
    await renderAt('/onaylarim/kuyruk')
    expect(await screen.findByTestId('work-queue-empty-readable')).toBeTruthy()
    expect(stateBlock('empty-no-data')).toBeNull()
  })

  it('a 403 goes through the auth boundary and shows no row', async () => {
    installFetch([APPROVE], () => json({ status: 403 }, 403))
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(document.querySelector('[data-state="forbidden"], [data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByTestId('work-queue-table')).toBeNull()
  })

  it.each([
    ['service-unavailable', () => json({ status: 503 }, 503)],
    ['malformed-response', () => json({ ...queue(), extra: true })],
    ['malformed-response', () => json(queue({ companyId: OTHER_COMPANY }))],
    ['malformed-response', () => json(queue({ items: [{ ...financeItem, link: 'https://example.invalid/x' }] }))],
  ])('shows the %s state and no row', async (kind, served) => {
    installFetch([APPROVE], served)
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(stateBlock(kind)).not.toBeNull())
    expect(screen.queryByTestId('work-queue-table')).toBeNull()
    expect(screen.queryAllByRole('link', { name: WORK_QUEUE_TEXT.openLabel(FINANCE_SOURCE.kindText) })).toEqual([])
  })

  it('a network failure is retryable and the retry reads afresh', async () => {
    let failing = true
    const { queueCalls } = installFetch([APPROVE], () => {
      if (failing) throw new TypeError('failed to fetch')
      return json(queue())
    })
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(stateBlock('network-error')).not.toBeNull())
    failing = false
    await userEvent.setup().click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByTestId('work-queue-table')).toBeTruthy()
    expect(queueCalls()).toHaveLength(2)
  })

  it('an actor with no connected owner\'s approve capability gets the neutral not-found page and nothing is read', async () => {
    const { queueCalls } = installFetch([CAPABILITY.cariPartiesList])
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(stateBlock('not-found')).not.toBeNull())
    expect(queueCalls()).toEqual([])
  })

  it('the inbox links to the queue', async () => {
    installFetch([APPROVE])
    const router = await renderAt('/onaylarim')
    await screen.findByRole('heading', { level: 1, name: APPROVALS_TEXT.heading })
    await userEvent.setup().click(screen.getByRole('link', { name: WORK_QUEUE_TEXT.openQueue }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/onaylarim/kuyruk'))
    expect(await screen.findByRole('heading', { level: 1, name: WORK_INBOX_TEXT.heading })).toBeTruthy()
  })
})

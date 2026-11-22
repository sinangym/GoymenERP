/**
 * WP1 (D5) one Work inbox in the REAL shell (router, CapabilityRoute, ShellProvider, auth boundary) over a stubbed fetch that
 * plays `GET /api/v1/work/inbox`, for an actor who holds only a native "Onaylarım" capability: the native D-05 kinds are
 * cards of the same list and the former B-25 queue (`GET /api/v1/work/queue`) is never read. Component evidence only: each
 * source's authority and the company fence are proven by the server's own tests (WorkInboxReadQueryV1Tests). Synthetic data.
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
import { APPROVALS_TEXT, WORK_QUEUE_TEXT } from './workText'
import { WORK_INBOX_TEXT } from './workInboxText'

const USER = '11111111-1111-1111-1111-111111111111'
const OTHER_COMPANY = '10000000-0000-0000-0000-000000000002'
const DIRECTORY = [
  { id: COMPANY, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false },
  { id: OTHER_COMPANY, code: 'GSP', legalName: 'Göymen B', isPlaceholder: false },
]
const APPROVE = CAPABILITY.financeOfficialInvoiceImportApprove
const IMPORT_KIND = 'finance.official-invoice-import.v1'
const COUNT_KIND = 'inventory.stock-count.v1'
const FIRST = 'c6000000-0000-4000-8000-000000000001'
const SECOND = 'c6000000-0000-4000-8000-000000000002'

const days = { class: 'BusinessDays', state: 'Defined', limit: 2, dueAtUtc: '2026-11-04T07:00:00+00:00', isOverdue: false, policyVersion: 'work.approval-duration-business-days:0', holidayCalendar: 'Defined' }
const importItem = {
  module: 'finance', kind: IMPORT_KIND, itemId: FIRST, documentLabel: `…${FIRST.slice(-12)}`, link: `/onaylarim/${IMPORT_KIND}/${FIRST}`,
  waitingSinceUtc: '2026-11-01T07:00:00+00:00', awaitingPermission: 'finance.official-invoice-import.approve', duration: days,
  ruleVersion: `work.approval-rule:${IMPORT_KIND}:0`,
  explanation: { rules: [{ ruleCode: 'finance.official-invoice-import-approval', threshold: null, actual: null, deviation: null, unit: null }], references: [] },
}
const countItem = {
  module: 'inventory', kind: COUNT_KIND, itemId: SECOND, documentLabel: 'ETK-0042', link: `/onaylarim/${COUNT_KIND}/${SECOND}`,
  waitingSinceUtc: '2026-11-02T07:00:00+00:00', awaitingPermission: 'inventory.stock-count.approve', duration: days,
  ruleVersion: `work.approval-rule:${COUNT_KIND}:0`,
  explanation: {
    rules: [{ ruleCode: 'inventory.stock-count-difference', threshold: null, actual: '98.5', deviation: '-1.5', unit: 'kg' }],
    references: [{ role: 'inventory-label', label: 'ETK-0042', recordId: 'e1000000-0000-4000-8000-000000000001', link: null }],
  },
}
const source = (module: string, kind: string, awaitingPermission: string, state = 'Ready', hasMore = false) =>
  ({ module, kind, awaitingPermission, durationClass: 'BusinessDays', ruleVersion: `work.approval-rule:${kind}:0`, state, hasMore })
const inbox = (overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY, view: 'awaiting-my-decision', generatedAtUtc: '2026-11-05T07:00:00+00:00',
  items: [importItem, countItem],
  sources: [source('finance', IMPORT_KIND, 'finance.official-invoice-import.approve'),
    source('inventory', COUNT_KIND, 'inventory.stock-count.approve')],
  ...overrides,
})

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function installFetch(granted: readonly string[], served: () => Response | Promise<Response> = () => json(inbox())) {
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
    if (url.includes('/work/inbox/events')) return json({ code: 'unavailable' }, 503)
    if (url.includes('/work/inbox')) return served()
    if (url.includes('/work/approvals')) return json({ companyId: COMPANY, items: [], nextCursor: null })
    return json({ code: 'unavailable' }, 503)
  }))
  return {
    inboxCalls: () => calls.filter((call) => call.url.includes('/work/inbox') && !call.url.includes('/events')),
    queueCalls: () => calls.filter((call) => call.url.includes('/work/queue')),
  }
}

async function renderAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
  await screen.findByText('Onaycı', undefined, { timeout: 5000 })
  return router
}

const stateBlock = (kind: string) => document.querySelector(`[data-state="${kind}"]`)
const cards = () => within(screen.getByTestId('work-inbox-list')).getAllByTestId('work-inbox-item')

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

describe('/onaylarim/kuyruk — the one Work inbox for a native "Onaylarım" capability holder', () => {
  it('lists the native kinds as cards of one list, oldest first, each linking to its owner\'s screen; no second list is read', async () => {
    const { inboxCalls, queueCalls } = installFetch([APPROVE])
    await renderAt('/onaylarim/kuyruk')
    expect(await screen.findByRole('heading', { level: 1, name: WORK_INBOX_TEXT.heading })).toBeTruthy()
    await screen.findByTestId('work-inbox-list')
    expect(cards().map((card) => card.getAttribute('data-kind'))).toEqual([IMPORT_KIND, COUNT_KIND])
    const count = cards()[1]
    expect(within(count).getByRole('heading', { level: 3, name: 'Stok sayım farkı onayı' })).toBeTruthy()
    expect(within(count).getByText('Stok')).toBeTruthy()
    expect(within(count).getByRole('link', { name: WORK_INBOX_TEXT.openOwnerLabel('Stok sayım farkı onayı', 'ETK-0042') })
      .getAttribute('href')).toBe(`/onaylarim/${COUNT_KIND}/${SECOND}`)
    // Read only: no approve control; one GET under the committed company and never the former B-25 queue.
    expect(screen.queryByRole('button', { name: /onayla/i })).toBeNull()
    expect(inboxCalls()).toEqual([{ url: '/api/v1/work/inbox?view=awaiting-my-decision', companyId: COMPANY }])
    expect(queueCalls()).toEqual([])
    expect(screen.queryByTestId('work-queue-legacy')).toBeNull()
  })

  it('an empty inbox is the no-data state', async () => {
    installFetch([APPROVE], () => json(inbox({ items: [] })))
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(stateBlock('empty-no-data')).not.toBeNull())
    expect(screen.queryByTestId('work-inbox-list')).toBeNull()
  })

  it('a 403 goes through the auth boundary and shows no card', async () => {
    installFetch([APPROVE], () => json({ status: 403 }, 403))
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(document.querySelector('[data-state="forbidden"], [data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByTestId('work-inbox-list')).toBeNull()
  })

  it.each([
    ['service-unavailable', () => json({ status: 503 }, 503)],
    ['malformed-response', () => json({ ...inbox(), extra: true })],
    ['malformed-response', () => json(inbox({ companyId: OTHER_COMPANY }))],
    ['malformed-response', () => json(inbox({ items: [{ ...importItem, link: 'https://example.invalid/x' }] }))],
    ['malformed-response', () => json(inbox({ items: [{ ...importItem, ruleVersion: `work.approval-rule:${COUNT_KIND}:0` }] }))],
  ])('shows the %s state and no card', async (kind, served) => {
    installFetch([APPROVE], served)
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(stateBlock(kind)).not.toBeNull())
    expect(screen.queryByTestId('work-inbox-list')).toBeNull()
  })

  it('a network failure is retryable and the retry reads afresh', async () => {
    let failing = true
    const { inboxCalls } = installFetch([APPROVE], () => {
      if (failing) throw new TypeError('failed to fetch')
      return json(inbox())
    })
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(stateBlock('network-error')).not.toBeNull())
    failing = false
    await userEvent.setup().click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByTestId('work-inbox-list')).toBeTruthy()
    expect(inboxCalls()).toHaveLength(2)
  })

  it('an actor with no connected owner\'s approve capability gets the neutral not-found page and nothing is read', async () => {
    const { inboxCalls, queueCalls } = installFetch([CAPABILITY.cariPartiesList])
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(stateBlock('not-found')).not.toBeNull())
    expect(inboxCalls()).toEqual([])
    expect(queueCalls()).toEqual([])
  })

  it('"Onaylarım" links to the one inbox', async () => {
    installFetch([APPROVE])
    const router = await renderAt('/onaylarim')
    await screen.findByRole('heading', { level: 1, name: APPROVALS_TEXT.heading })
    await userEvent.setup().click(screen.getByRole('link', { name: WORK_QUEUE_TEXT.openQueue }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/onaylarim/kuyruk'))
    expect(await screen.findByRole('heading', { level: 1, name: WORK_INBOX_TEXT.heading })).toBeTruthy()
  })
})

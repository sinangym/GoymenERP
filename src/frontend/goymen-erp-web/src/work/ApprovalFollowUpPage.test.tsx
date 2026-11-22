import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { WORK_CAPABILITY } from './capabilities'
import { APPROVAL_FOLLOW_UP_TEXT as T } from './approvalFollowUpText'
import { decodeApprovalFollowUpPage } from './approvalFollowUpContract'

/** SENTETIK data only. */
const ITEM = 'cccccccc-0000-4000-8000-0000000000f1'
const item = (overrides: Record<string, unknown> = {}) => ({
  kind: 'logistics.dispatch-note.v1', itemId: ITEM, documentLabel: 'SENTETIK irsaliye', link: `/lojistik/yukleme-talimatlari/${ITEM}`,
  waitingSinceUtc: '2026-11-23T06:00:00+00:00', step: 'escalated', approverPermission: 'logistics.dispatch-notes.approve',
  escalationPermission: 'work.approvals.escalate', ruleVersion: 'work.approval-rule:logistics.dispatch-note.v1:0',
  dueAtUtc: '2026-11-23T10:00:00+00:00', recordedAtUtc: '2026-11-23T14:01:00+00:00', recipientCount: 1, ...overrides,
})
const page = (view: string, items: unknown[]) => ({ companyId: COMPANY_A, view, items, hasMore: false })

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear() })

function mount() {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: workRoutes }], { initialEntries: ['/onay-takibi'] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
}

describe('WP2 approval follow-up decoder', () => {
  it('accepts exact keys only and the view each item belongs to', () => {
    expect(decodeApprovalFollowUpPage(page('escalated', [item()]), COMPANY_A, 'escalated')?.items[0]?.itemId).toBe(ITEM)
    expect(decodeApprovalFollowUpPage(page('escalated', [item({ step: 'orphaned' })]), COMPANY_A, 'escalated')).toBeNull()
    expect(decodeApprovalFollowUpPage(page('escalated', [item({ escalationPermission: null })]), COMPANY_A, 'escalated')).toBeNull()
    expect(decodeApprovalFollowUpPage(page('escalated', [item({ extra: true })]), COMPANY_A, 'escalated')).toBeNull()
    expect(decodeApprovalFollowUpPage(page('escalated', [item({ link: 'https://example.invalid' })]), COMPANY_A, 'escalated')).toBeNull()
    expect(decodeApprovalFollowUpPage(page('orphaned', [item({ step: 'orphaned', escalationPermission: null, dueAtUtc: null, recipientCount: 0 })]),
      COMPANY_A, 'orphaned')).not.toBeNull()
    expect(decodeApprovalFollowUpPage({ ...page('escalated', []), companyId: 'bbbbbbbb-0000-4000-8000-000000000001' }, COMPANY_A, 'escalated')).toBeNull()
  })
})

describe('WP2 "Onay takibi" page', () => {
  it('lists escalated approvals with the owner link and switches to the orphaned view', async () => {
    const calls = installFetch({ capabilities: [WORK_CAPABILITY.approvalFollowUp], respond: (url) => {
      if (url.includes('/work/approval-follow-ups?view=escalated')) return json(page('escalated', [item()]))
      if (url.includes('/work/approval-follow-ups?view=orphaned')) return json(page('orphaned', []))
      return undefined
    } })
    mount()
    expect(await screen.findByText('SENTETIK irsaliye')).toBeTruthy()
    expect(screen.getByRole('link', { name: T.open }).getAttribute('href')).toBe(`/lojistik/yukleme-talimatlari/${ITEM}`)
    await userEvent.setup().click(screen.getByRole('button', { name: T.viewOrphaned }))
    expect(await screen.findByText(T.emptyOrphaned)).toBeTruthy()
    expect(calls.filter((call) => call.url.includes('/work/approval-follow-ups')).every((call) => call.headers['X-Company-Id'] === COMPANY_A)).toBe(true)
  })

  it('reads nothing without the capability and shows the service failure as a state', async () => {
    const denied = installFetch({ capabilities: [], respond: (url) =>
      url.includes('/work/approval-follow-ups') ? json(page('escalated', [item()])) : undefined })
    mount()
    await waitFor(() => expect(denied.some((call) => call.url.endsWith('/shell/capabilities'))).toBe(true))
    await waitFor(() => expect(screen.queryByRole('heading', { name: T.heading })).toBeNull())
    expect(screen.queryByText('SENTETIK irsaliye')).toBeNull()
    expect(denied.some((call) => call.url.includes('/work/approval-follow-ups'))).toBe(false)
    cleanup()
    installFetch({ capabilities: [WORK_CAPABILITY.approvalFollowUp], respond: (url) =>
      url.includes('/work/approval-follow-ups') ? problem(503) : undefined })
    mount()
    expect(await screen.findByRole('button', { name: 'Tekrar dene' })).toBeTruthy()
  })
})

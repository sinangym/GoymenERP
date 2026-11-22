import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { ASSISTANT_MEMORY_REASON_TEXT as REASON, ASSISTANT_MEMORY_TEXT as T } from './assistantMemoryText'

/** WP-C6 (AI-D30). SENTETIK data only. */
const ACTOR = '99999999-9999-4999-8999-999999999999'
const OTHER = 'eeeeeeee-0000-4000-8000-0000000000c3'
const ENTRY = 'ffffffff-0000-4000-8000-0000000000c6'
const entry = (overrides: Record<string, unknown> = {}) => ({
  id: ENTRY, companyId: COMPANY_A, ownerUserId: ACTOR, kind: 'preference', body: 'SENTETIK kisa cevap', state: 'active', version: '1',
  createdAtUtc: '2026-11-22T09:00:00+00:00', updatedAtUtc: '2026-11-22T09:00:00+00:00', expiresAtUtc: '2026-12-22T09:00:00+00:00',
  closedAtUtc: null, closeReason: null, ...overrides,
})
const page = (items: unknown[]) => ({ companyId: COMPANY_A, ownerUserId: ACTOR, items, hasMore: false })
const status = (writable: boolean) => ({
  companyId: COMPANY_A, userId: ACTOR, writable, disabledReasons: writable ? [] : ['pilot_off', 'information_notice_pending'],
  pilotEnabled: writable, retentionDays: writable ? 30 : null, maxEntriesPerUser: writable ? 5 : null,
  informationNoticeVersion: writable ? 'SENTETIK-NOTICE-1' : null, activeEntryCount: 1,
})

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear() })

function mount() {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: workRoutes }], { initialEntries: ['/asistan-hafizam'] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
}

describe('WP-C6 assistant memory page', () => {
  it('with the pilot closed shows why, offers no new-entry form and still lets the owner delete an entry', async () => {
    let deleted = false
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.endsWith(`/entries/${ENTRY}/deletion`) && init?.method === 'POST') {
        deleted = true
        return json({ id: ENTRY, version: '2', deletedAtUtc: '2026-11-22T10:00:00+00:00' })
      }
      if (url.includes('/assistant-memory/mine/status')) return json(status(false))
      if (url.includes('/assistant-memory/mine/entries?')) return json(page(deleted ? [] : [entry()]))
      return undefined
    } })
    mount()
    expect(await screen.findByText('SENTETIK kisa cevap')).toBeTruthy()
    expect(await screen.findByText(REASON.pilot_off)).toBeTruthy()
    expect(screen.getByText(T.noticePlaceholder)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: T.remove }))
    await user.click(screen.getByRole('button', { name: T.confirmRemove }))
    expect(await screen.findByText(T.removed)).toBeTruthy()
    const post = calls.find((call) => call.method === 'POST')!
    expect(post.body).toEqual({ expectedVersion: '1' })
    expect(post.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(post.headers['X-Company-Id']).toBe(COMPANY_A)
    await waitFor(() => expect(screen.queryByText('SENTETIK kisa cevap')).toBeNull())
  })

  it('saves a new entry with one key, reuses it after an unknown outcome and shows the limit refusal by its code', async () => {
    let attempt = 0
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.endsWith('/assistant-memory/mine/entries') && init?.method === 'POST') {
        attempt += 1
        return attempt === 1 ? problem(503) : problem(409, { code: 'work_assistant_memory_limit_reached' })
      }
      if (url.includes('/assistant-memory/mine/status')) return json(status(true))
      if (url.includes('/assistant-memory/mine/entries?')) return json(page([]))
      return undefined
    } })
    mount()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.bodyLabel), 'SENTETIK tercih')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.limitReached)).toBeTruthy()
    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts.map((call) => call.body)).toEqual([{ kind: 'preference', body: 'SENTETIK tercih' }, { kind: 'preference', body: 'SENTETIK tercih' }])
    expect(posts[0].headers['Idempotency-Key']).toBe(posts[1].headers['Idempotency-Key'])
  })

  it('revises an entry with its expected version and never asks who viewed the memory (AI-D39)', async () => {
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.endsWith(`/entries/${ENTRY}/revision`) && init?.method === 'POST') return json(entry({ body: 'SENTETIK duzeltildi', version: '2' }))
      if (url.includes('/assistant-memory/mine/status')) return json(status(true))
      if (url.includes('/assistant-memory/mine/entries?')) return json(page([entry()]))
      return undefined
    } })
    mount()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.edit }))
    const input = screen.getAllByLabelText(T.bodyLabel).find((element) => (element as HTMLInputElement).value === 'SENTETIK kisa cevap')!
    await user.clear(input)
    await user.type(input, 'SENTETIK duzeltildi')
    await user.click(screen.getByRole('button', { name: T.saveEdit }))
    expect(await screen.findByText(T.revised)).toBeTruthy()
    expect(calls.find((call) => call.method === 'POST')!.body).toEqual({ expectedVersion: '1', kind: 'preference', body: 'SENTETIK duzeltildi' })
    expect(calls.some((call) => call.url.includes('oversight'))).toBe(false)
  })

  it('treats another persons entry page as malformed and shows none of its text', async () => {
    installFetch({ capabilities: [], respond: (url) => {
      if (url.includes('/assistant-memory/mine/status')) return json(status(true))
      if (url.includes('/assistant-memory/mine/entries?')) return json({ ...page([entry({ ownerUserId: OTHER })]), ownerUserId: OTHER })
      return undefined
    } })
    mount()
    expect(await screen.findByRole('heading', { name: T.heading, level: 1 })).toBeTruthy()
    await waitFor(() => expect(screen.queryByText(T.writableYes)).toBeTruthy())
    expect(screen.queryByText('SENTETIK kisa cevap')).toBeNull()
  })
})

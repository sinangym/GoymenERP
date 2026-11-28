import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { CAPABILITY } from '../shell/capabilities'
import { installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { WORK_MANAGEMENT_TEXT as T } from './workManagementText'
import { CARI_MERGE_CAPABILITY } from '../cari/mergeImpact'
const clients: QueryClient[] = []
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } }); clients.push(client)
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: workRoutes }], { initialEntries: ['/calisma-yonetimi'] })
  render(<ShellProvider queryClient={client}><RouterProvider router={router} /></ShellProvider>)
}
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear() })
describe('native single-step merge entry from Work management', () => {
  it('opens only the existing Cari list with BOTH published merge and native-list capabilities, without creating pending work or reading impact', async () => {
    const calls = installFetch({ capabilities: [CARI_MERGE_CAPABILITY, CAPABILITY.cariPartiesList] })
    mount()
    const link = await screen.findByRole('link', { name: T.mergeEntry })
    expect(link.getAttribute('href')).toBe('/cari'); expect(screen.getByText(T.mergeHint)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull()
    expect(calls.filter(call => call.method !== 'GET')).toHaveLength(0)
    expect(calls.filter(call => call.url.includes('/work/inbox') || call.url.includes('/merge/preview'))).toHaveLength(0)
  })
  it.each([[CAPABILITY.cariPartiesList], [CARI_MERGE_CAPABILITY], ['work.delegations.manage', CAPABILITY.cariPartiesList]])('does not expose the entry for incomplete capabilities %j', async capabilities => {
    installFetch({ capabilities, respond: url => url.includes('/work/delegations?') ? json({ companyId: 'aaaaaaaa-0000-4000-8000-000000000001', items: [], page: 0, hasMore: false, people: [] }) : undefined })
    mount(); if (capabilities.includes(CARI_MERGE_CAPABILITY) || capabilities.includes('work.delegations.manage')) await screen.findByRole('heading', { name: T.title })
    else await screen.findByRole('alert')
    expect(screen.queryByRole('link', { name: T.mergeEntry })).toBeNull()
  })
  it('does not expose the entry when the server cannot release current capabilities', async () => {
    installFetch({ respond: url => url.endsWith('/shell/capabilities') ? problem(503) : undefined })
    // Override only the capability response while keeping the actual identity/company bootstrap.
    const original = globalThis.fetch
    vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => String(input).endsWith('/shell/capabilities') ? Promise.resolve(problem(503)) : original(input, init))
    mount(); await screen.findByRole('alert')
    expect(screen.queryByRole('link', { name: T.mergeEntry })).toBeNull()
  })
})

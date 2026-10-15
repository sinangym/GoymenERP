/**
 * AT-002 — the global search's "Tümünü listede gör" continues in C-01: an entry carrying `?q=` starts the Cari list from
 * that term on the default filters (keeping the user's sort and page size); an entry without one restores the saved view
 * unchanged. Component evidence, NOT_ENV.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, waitFor } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { DEFAULT_CARI_LIST_VIEW, resetCariListView, withEntryQuery, type CariListView } from './listViewState'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const IDENTITY = { userId: '99999999-9999-9999-9999-999999999999', displayName: 'Kullanıcı A', email: null, allowedCompanyIds: [A_ID], authenticationSessionBinding: null }
const DIRECTORY = [{ id: A_ID, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true }]

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

function installFetch() {
  const reads: string[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url.includes('/identity/me')) return json(IDENTITY)
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities')) {
        return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'gen-1', capabilities: [CAPABILITY.cariPartiesList] })
      }
      if (url.includes('/counterparties')) {
        reads.push(url)
        return json({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null })
      }
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  return reads
}

function renderAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  render(
    <ShellProvider queryClient={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
}

beforeEach(() => resetCariListView())

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AT-002 Cari list entry from the global search', () => {
  it('starts from the entry term on the default filters, keeping sort and page size', () => {
    const saved: CariListView = { ...DEFAULT_CARI_LIST_VIEW, q: 'eski', kind: 'LegalEntity', partyState: 'all', sort: 'legalName', pageSize: 25 }
    expect(withEntryQuery(saved, '  Zeta ')).toEqual({ ...DEFAULT_CARI_LIST_VIEW, q: 'Zeta', sort: 'legalName', pageSize: 25 })
    expect(withEntryQuery(saved, null)).toBe(saved)
    expect(withEntryQuery(saved, '   ')).toBe(saved)
  })

  it('reads the list with the entry term from the URL', async () => {
    const reads = installFetch()
    renderAt('/cari?q=Zeta')
    await waitFor(() => expect(reads.length).toBeGreaterThan(0))
    expect(new URL(reads[0], 'http://x').searchParams.get('q')).toBe('Zeta')
  })

  it('reads the list without a term when the entry carries none', async () => {
    const reads = installFetch()
    renderAt('/cari')
    await waitFor(() => expect(reads.length).toBeGreaterThan(0))
    expect(new URL(reads[0], 'http://x').searchParams.get('q')).toBeNull()
  })
})

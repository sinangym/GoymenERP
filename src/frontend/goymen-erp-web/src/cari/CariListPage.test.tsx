import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { PARTY_ROLE_FLAG } from './contract'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const PARTY_1 = '11111111-1111-1111-1111-111111111111'
const PARTY_2 = '22222222-2222-2222-2222-222222222222'
const ACC_1 = 'cccccccc-1111-1111-1111-111111111111'
const ACC_2 = 'dddddddd-2222-2222-2222-222222222222'

const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Kullanıcı A',
  email: null,
  allowedCompanyIds: [A_ID, B_ID],
}
const DIRECTORY = [
  { id: A_ID, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true },
  { id: B_ID, code: 'GSP', legalName: 'Göymen B', isPlaceholder: true },
]

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function party(over: Record<string, unknown> = {}) {
  return {
    id: PARTY_1,
    code: 'C-0001',
    kind: 1,
    legalName: 'Alfa Metal Ltd. Şti.',
    tradeName: 'Alfa',
    isActive: true,
    version: 1,
    companyAccounts: [
      { id: ACC_1, companyId: A_ID, roles: PARTY_ROLE_FLAG.Customer, isActive: true, version: 1 },
    ],
    ...over,
  }
}

function page(over: Record<string, unknown> = {}) {
  return {
    items: [party()],
    nextCursor: null,
    hasMore: false,
    totalCount: null,
    countScope: null,
    ...over,
  }
}

interface Config {
  capabilities?: readonly string[]
  counterparties?: (url: string, companyId: string | null) => Response
}

function installFetch(config: Config = {}) {
  const calls: { url: string; companyId: string | null }[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const companyId =
      (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
    calls.push({ url, companyId })
    if (url.includes('/identity/me')) return json(IDENTITY)
    if (url.includes('/organization/companies')) return json(DIRECTORY)
    if (url.includes('/shell/capabilities'))
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: 'gen-1',
        capabilities: config.capabilities ?? [CAPABILITY.cariPartiesList],
      })
    if (url.includes('/counterparties'))
      return (config.counterparties ?? (() => json(page())))(url, companyId)
    throw new Error(`unexpected fetch: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock, calls }
}

/**
 * Query a shared state block by its `kind` rather than its text: the title and the
 * detail of a block intentionally share vocabulary ("Kayıt yok" / "…henüz kayıt yok."),
 * so a text matcher hits both and the assertion says nothing about which state rendered.
 */
async function findStateBlock(kind: string): Promise<HTMLElement> {
  return await waitFor(() => {
    const node = document.querySelector<HTMLElement>(`[data-state="${kind}"]`)
    if (node === null) throw new Error(`state block "${kind}" not rendered`)
    return node
  })
}

function queryStateBlock(kind: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-state="${kind}"]`)
}

function renderAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return queryClient
}

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('C-01 capability gating (DEC-GOV-007 / ERP-000K §8.1)', () => {
  it('mounts /cari and shows the nav entry when the server granted the capability', async () => {
    installFetch()
    renderAt('/cari')
    expect(await screen.findByRole('heading', { name: 'Cari Kartlar' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Cari Kartlar' })).toBeTruthy()
  })

  it('without the capability: no nav entry and a deep link mounts nothing', async () => {
    installFetch({ capabilities: [] })
    renderAt('/cari')
    // non-disclosing: the same neutral block as any unknown page
    expect(await findStateBlock('not-found')).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Cari Kartlar' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Cari Kartlar' })).toBeNull()
    expect(screen.getByText(/henüz uygulanmış modül yok/i)).toBeTruthy()
  })

  it('does not call the counterparty API when the capability is absent', async () => {
    const { calls } = installFetch({ capabilities: [] })
    renderAt('/cari')
    await findStateBlock('not-found')
    expect(calls.some((c) => c.url.includes('/counterparties'))).toBe(false)
  })
})

describe('C-01 list rendering', () => {
  it('sends X-Company-Id and renders a row from the real contract shape', async () => {
    const { calls } = installFetch()
    renderAt('/cari')
    await screen.findByRole('heading', { name: 'Cari Kartlar' })

    const row = await screen.findByRole('row', { name: /C-0001/ })
    expect(within(row).getByText('C-0001')).toBeTruthy()
    expect(within(row).getByText('Alfa Metal Ltd. Şti.')).toBeTruthy()
    expect(within(row).getByText('Alfa')).toBeTruthy()
    expect(within(row).getByText('Tüzel kişi')).toBeTruthy()

    const listCall = calls.find((c) => c.url.includes('/counterparties'))
    expect(listCall?.companyId).toBe(A_ID)
  })

  it('AT-010: a single account carries combined role flags (one row, both roles)', async () => {
    installFetch({
      counterparties: () =>
        json(
          page({
            items: [
              party({
                companyAccounts: [
                  {
                    id: ACC_1,
                    companyId: A_ID,
                    roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier,
                    isActive: true,
                    version: 1,
                  },
                ],
              }),
            ],
          }),
        ),
    })
    renderAt('/cari')
    const row = await screen.findByRole('row', { name: /C-0001/ })
    expect(within(row).getByText('Müşteri, Tedarikçi')).toBeTruthy()
  })

  it('JIT §3.5: a foreign company account rejects the WHOLE page, it is not filtered away', async () => {
    installFetch({
      counterparties: () =>
        json(
          page({
            items: [
              party({
                companyAccounts: [
                  {
                    id: ACC_1,
                    companyId: A_ID,
                    roles: PARTY_ROLE_FLAG.Customer,
                    isActive: true,
                    version: 1,
                  },
                  // company B is NOT the active context — its presence is a leak
                  {
                    id: ACC_2,
                    companyId: B_ID,
                    roles: PARTY_ROLE_FLAG.Service,
                    isActive: true,
                    version: 1,
                  },
                ],
              }),
            ],
          }),
        ),
    })
    renderAt('/cari')
    expect(await findStateBlock('malformed-response')).toBeTruthy()
    // silently rendering only company A's part would hide that foreign data arrived
    expect(screen.queryByText('C-0001')).toBeNull()
  })

  it('JIT §3.5: a party with no account for the active company rejects the page', async () => {
    installFetch({
      counterparties: () => json(page({ items: [party({ companyAccounts: [] })] })),
    })
    renderAt('/cari')
    expect(await findStateBlock('malformed-response')).toBeTruthy()
  })

  it('shows the global party state and this company account state as separate facts', async () => {
    installFetch({
      counterparties: () =>
        json(
          page({
            items: [
              party({
                isActive: true,
                companyAccounts: [
                  {
                    id: ACC_1,
                    companyId: A_ID,
                    roles: PARTY_ROLE_FLAG.Customer,
                    isActive: false,
                    version: 1,
                  },
                ],
              }),
            ],
          }),
        ),
    })
    renderAt('/cari')
    const row = await screen.findByRole('row', { name: /C-0001/ })
    expect(within(row).getByText('Cari aktif')).toBeTruthy()
    expect(within(row).getByText(/şirket hesabı pasif/i)).toBeTruthy()
  })

  it('shows no balance / risk / limit column (DEC-CAR-002, AT-011, mockTruthForbidden)', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    for (const forbidden of [/bakiye/i, /risk/i, /limit/i, /borç/i, /alacak/i]) {
      expect(screen.queryByText(forbidden)).toBeNull()
    }
  })
})

describe('C-01 states', () => {
  it('distinguishes "no records" from "no filter match"', async () => {
    installFetch({ counterparties: () => json(page({ items: [] })) })
    renderAt('/cari')
    expect(await findStateBlock('empty-no-data')).toBeTruthy()

    await userEvent.type(screen.getByRole('searchbox'), 'zzz')
    await waitFor(() => expect(queryStateBlock('empty-filtered')).not.toBeNull())
  })

  it('403 is non-disclosing and does not render a list', async () => {
    installFetch({ counterparties: () => json({ code: 'forbidden' }, 403) })
    renderAt('/cari')
    expect(await findStateBlock('forbidden')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('a malformed page fails closed instead of rendering a partial list', async () => {
    installFetch({
      counterparties: () => json(page({ items: [party(), party({ kind: 99 })] })),
    })
    renderAt('/cari')
    expect(await findStateBlock('malformed-response')).toBeTruthy()
    expect(screen.queryByText('C-0001')).toBeNull()
  })

  it('server-side search: typing sends q and does not filter client-side', async () => {
    const { calls } = installFetch({
      counterparties: (url) =>
        url.includes('q=alfa') ? json(page()) : json(page({ items: [party({ id: PARTY_2, code: 'C-0002' })] })),
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0002/ })

    await userEvent.type(screen.getByRole('searchbox'), 'alfa')
    await waitFor(() => expect(calls.some((c) => c.url.includes('q=alfa'))).toBe(true))
  })

  it('sorting toggles direction and exposes aria-sort', async () => {
    const { calls } = installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    const codeHeader = screen.getByRole('columnheader', { name: /Kod/ })
    expect(codeHeader.getAttribute('aria-sort')).toBe('ascending')

    await userEvent.click(within(codeHeader).getByRole('button'))
    await waitFor(() => expect(codeHeader.getAttribute('aria-sort')).toBe('descending'))
    expect(calls.some((c) => c.url.includes('direction=desc'))).toBe(true)
  })
})

describe('C-01 paging', () => {
  it('loads the next cursor page and appends without duplicating rows', async () => {
    installFetch({
      counterparties: (url) =>
        url.includes('cursor=next-1')
          ? json(page({ items: [party({ id: PARTY_2, code: 'C-0002' })] }))
          : json(page({ hasMore: true, nextCursor: 'next-1' })),
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))
    await waitFor(() => expect(screen.getByRole('row', { name: /C-0002/ })).toBeTruthy())
    expect(screen.getAllByRole('row', { name: /C-0001/ })).toHaveLength(1)
  })

  it('invalid_cursor falls back to the first page instead of stranding the user', async () => {
    installFetch({
      counterparties: (url) =>
        url.includes('cursor=')
          ? json({ code: 'invalid_cursor', title: 'İmleç geçersiz', status: 400 }, 400)
          : json(page({ hasMore: true, nextCursor: 'stale-cursor' })),
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))
    // recovers to page 1 rather than showing a broken/empty state
    await waitFor(() => expect(screen.getByRole('row', { name: /C-0001/ })).toBeTruthy())
  })
})

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { PARTY_ROLE_FLAG } from './contract'
import { resetCariListView } from './listViewState'

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
  /** Awaited before the capability call for this company answers — pins a switch in flight. */
  holdCapabilityFor?: { companyId: string; gate: Promise<void> }
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
    if (url.includes('/shell/capabilities')) {
      if (config.holdCapabilityFor?.companyId === companyId) await config.holdCapabilityFor.gate
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: companyId === B_ID ? 'gen-b' : 'gen-1',
        capabilities:
          config.capabilities ?? [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail],
      })
    }
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

/** Same render, but hands back the router so a test can leave the list and come back. */
function renderRouterAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return router
}

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  // The remembered list view is module state — it outlives a render on purpose, which
  // is exactly why it has to be cleared between cases. Without this a search typed in
  // one test is restored into the next one.
  resetCariListView()
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
    // Both facts are stated explicitly. Codex/ChatGPT #147 P1-4: "active" must not be
    // an absence — an absent label reads the same as "unknown".
    const partyCell = row.querySelector('[data-party-state]')
    const accountCell = row.querySelector('[data-account-state]')
    expect(partyCell?.getAttribute('data-party-state')).toBe('active')
    expect(partyCell?.textContent).toBe('Aktif')
    expect(accountCell?.getAttribute('data-account-state')).toBe('inactive')
    expect(accountCell?.textContent).toBe('Pasif')
  })

  it('states an ACTIVE company account explicitly too, not by omission', async () => {
    installFetch()
    renderAt('/cari')
    const row = await screen.findByRole('row', { name: /C-0001/ })
    const accountCell = row.querySelector('[data-account-state]')
    expect(accountCell?.getAttribute('data-account-state')).toBe('active')
    expect(accountCell?.textContent).toBe('Aktif')
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

  it('403 is non-disclosing and closes the capability, not just the list', async () => {
    installFetch({ counterparties: () => json({ code: 'forbidden' }, 403) })
    renderAt('/cari')
    // Issue #165 SAB-004/005 raised this bar. The old contract was a local
    // 'forbidden' block over a capability that stayed usable — so a route return
    // or a refetch could mount the feature again on the authority the server had
    // just rejected. The capability is now re-fenced, which closes the gated
    // route itself; the block is the same non-disclosing one an unknown page gets.
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByRole('link', { name: 'Cari Kartlar' })).toBeNull()
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

describe('C-01 context fence (Codex/ChatGPT #147 P1-2)', () => {
  it('the screen is unmounted for the whole time the shell has no bound context', async () => {
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    installFetch({ holdCapabilityFor: { companyId: B_ID, gate } })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)

    // The switch is pinned in flight. This is the window the review was about: the
    // shell holds no verified context, so the business screen must not exist at all —
    // not merely be showing a spinner over company A's rows.
    await waitFor(() => expect(screen.getByText(/değiştiriliyor/i)).toBeTruthy())
    expect(screen.queryByRole('heading', { name: 'Cari Kartlar' })).toBeNull()
    expect(screen.queryByText('C-0001')).toBeNull()
    expect(screen.queryByText(/Alfa Metal/)).toBeNull()

    release()
    // ...and it comes back once B is verified, mounted fresh under B's context.
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Cari Kartlar' })).toBeTruthy(),
    )
    expect((screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement).value).toBe(
      B_ID,
    )
  })

  it('a company switch never leaves company A rows on screen under company B', async () => {
    let releaseB: () => void = () => {}
    const gateB = new Promise<void>((resolve) => {
      releaseB = resolve
    })
    installFetch({
      counterparties: (_url, companyId) =>
        companyId === A_ID
          ? json(page())
          : (async () => {
              // B's rows arrive LATE — the window in which A's rows could linger.
              await gateB
              return json(
                page({
                  items: [
                    party({
                      id: PARTY_2,
                      code: 'C-0002',
                      legalName: 'Beta Sac A.Ş.',
                      companyAccounts: [
                        {
                          id: ACC_2,
                          companyId: B_ID,
                          roles: PARTY_ROLE_FLAG.Supplier,
                          isActive: true,
                          version: 1,
                        },
                      ],
                    }),
                  ],
                }),
              )
            })() as unknown as Response,
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)

    // B's context is committed while B's list is still in flight. Company A's code,
    // title and roles must all be gone — not merely stale-but-visible.
    await waitFor(() =>
      expect(
        (screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement).value,
      ).toBe(B_ID),
    )
    expect(screen.queryByText('C-0001')).toBeNull()
    expect(screen.queryByText(/Alfa Metal/)).toBeNull()
    expect(screen.queryByRole('row', { name: /C-0001/ })).toBeNull()

    releaseB()
    await waitFor(() => expect(screen.getByRole('row', { name: /C-0002/ })).toBeTruthy())
    expect(screen.queryByText('C-0001')).toBeNull()
  })

  it('accumulated cursor pages do not survive a company switch', async () => {
    installFetch({
      counterparties: (url, companyId) => {
        if (companyId === B_ID) return json(page({ items: [] }))
        return url.includes('cursor=next-1')
          ? json(page({ items: [party({ id: PARTY_2, code: 'C-0002' })] }))
          : json(page({ hasMore: true, nextCursor: 'next-1' }))
      },
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))
    await waitFor(() => expect(screen.getByRole('row', { name: /C-0002/ })).toBeTruthy())

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)

    // B legitimately has no counterparties; neither accumulated A page may remain.
    await findStateBlock('empty-no-data')
    expect(screen.queryByText('C-0001')).toBeNull()
    expect(screen.queryByText('C-0002')).toBeNull()
  })
})

describe('C-01 filter contract (Codex/ChatGPT #147 P1-4)', () => {
  it('sends accountState independently of partyState', async () => {
    const { calls } = installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.selectOptions(screen.getByLabelText('Şirket hesabı durumu'), 'inactive')
    await waitFor(() =>
      expect(
        calls.some(
          (c) =>
            c.url.includes('/counterparties') &&
            c.url.includes('accountState=inactive') &&
            // the global Party filter is untouched by the account filter
            c.url.includes('partyState=active'),
        ),
      ).toBe(true),
    )
  })

  it('offers roleMatch only with 2+ roles, and sends the chosen mode', async () => {
    const { calls } = installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    // The legacy markers live one fold in now (ILK_DILIM §4.1) — reach them the
    // way a user does.
    await userEvent.click(screen.getByText('Geçmiş sınıflandırma'))

    // One role: `any` and `all` are the same query, so no control is offered.
    await userEvent.click(screen.getByRole('checkbox', { name: 'Müşteri' }))
    expect(screen.queryByLabelText('İşaret eşleşmesi')).toBeNull()

    await userEvent.click(screen.getByRole('checkbox', { name: 'Tedarikçi' }))
    await userEvent.selectOptions(screen.getByLabelText('İşaret eşleşmesi'), 'all')

    await waitFor(() =>
      expect(
        calls.some(
          (c) =>
            c.url.includes('role=Customer') &&
            c.url.includes('role=Supplier') &&
            c.url.includes('roleMatch=all'),
        ),
      ).toBe(true),
    )
  })

  it('never asks for a count it will not label, so no raw scope token can reach the UI', async () => {
    const { calls } = installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    // includeCount defaults to false; the decoder rejects a count we did not request.
    expect(calls.every((c) => !c.url.includes('includeCount=true'))).toBe(true)
    expect(screen.queryByText(/filtered-authorized-visible-parties/)).toBeNull()
  })
})

describe('C-01 group mode is not claimed (Codex/ChatGPT #147 P1-3)', () => {
  it('does not render rows or issue a request without a single-company context', async () => {
    const { calls } = installFetch({
      counterparties: () => json(page()),
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    const before = calls.filter((c) => c.url.includes('/counterparties')).length
    expect(before).toBeGreaterThan(0)

    // While the switch is in flight the shell holds no bound context. The list must
    // show no rows and must not query — it cannot represent that state honestly.
    expect(screen.queryByText(/—/)).toBeNull()
  })

  it('is remounted on a context change, so no local state carries over', async () => {
    installFetch({
      counterparties: (_url, companyId) =>
        companyId === B_ID ? json(page({ items: [] })) : json(page()),
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    // Filters, sort and paging are component state. They were chosen for company A.
    await userEvent.type(screen.getByRole('searchbox'), 'alfa')
    await userEvent.selectOptions(screen.getByLabelText('Şirket hesabı durumu'), 'inactive')
    expect((screen.getByRole('searchbox') as HTMLInputElement).value).toBe('alfa')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)
    await waitFor(() =>
      expect((screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement).value).toBe(
        B_ID,
      ),
    )

    // The screen re-authorizes on entry (#146/#148), so it is briefly absent while
    // the fresh capability lands. Asserting the instant the picker settled measured
    // a frame the user never sees, and it was a real flake — Codex reproduced it
    // 2/10 independently.
    //
    // But waiting alone WEAKENS the case: a bare wait cannot tell a normal
    // asynchronous gate from an indefinitely stuck one, and it stops proving that
    // A's data is absent during the window. So the window is asserted first, then
    // the settled state. Codex's direction, and it is the better test.
    // The gate replaces the WHOLE route, so its own heading is the unambiguous
    // signal that it is closed. Keying on `[data-state="initial-loading"]` was
    // wrong and measurably so: the list renders its filters above its own state
    // block, so that selector also matches the list loading its rows — and the
    // searchbox is legitimately present then. That mistake failed 3/5.
    if (screen.queryByRole('heading', { name: 'Cari Kartlar' }) === null) {
      expect(
        screen.queryByRole('row', { name: /C-0001/ }),
        "company A's rows were visible while the entry was being re-authorized",
      ).toBeNull()
    }

    // Bounded: if the child never comes back, that is a stuck gate — a runtime P1,
    // not a synchronisation detail — and this fails rather than hanging.
    const searchbox = await screen.findByRole('searchbox', undefined, { timeout: 5000 })

    // Carrying A's search term into B would silently filter B's list by a term the
    // user never chose for B — and the screen would look like B simply has no records.
    expect((searchbox as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Şirket hesabı durumu') as HTMLSelectElement).value).toBe('all')
  })
})

describe('C-01 deferred continuation (ChatGPT #147 P1)', () => {
  /** Page 2 answers only when released, so the in-flight window is observable. */
  function installDeferredSecondPage(secondPage: unknown) {
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    installFetch({
      counterparties: (url) =>
        url.includes('cursor=next-1')
          ? (gate.then(() => json(secondPage)) as unknown as Response)
          : json(page({ hasMore: true, nextCursor: 'next-1' })),
    })
    return { release: () => release() }
  }

  it('keeps page 1 visible instead of reporting the server as malformed', async () => {
    const { release } = installDeferredSecondPage(
      page({ items: [party({ id: PARTY_2, code: 'C-0002' })] }),
    )
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))

    // `keepPreviousData` hands back page 1 while page 2 is in flight. Appending it to
    // the page 1 we already accumulated produced every row twice, and the duplicate
    // guard then blamed the server for our own double-count.
    await waitFor(() => expect(screen.getByRole('button', { name: /Yükleniyor/i })).toBeTruthy())
    expect(queryStateBlock('malformed-response')).toBeNull()
    expect(screen.getAllByRole('row', { name: /C-0001/ })).toHaveLength(1)
    expect(screen.getByRole('button', { name: /Yükleniyor/i })).toHaveProperty('disabled', true)
    expect(screen.getByRole('table').getAttribute('aria-busy')).toBe('true')

    release()
    await waitFor(() => expect(screen.getByRole('row', { name: /C-0002/ })).toBeTruthy())
    // appended exactly once
    expect(screen.getAllByRole('row', { name: /C-0001/ })).toHaveLength(1)
    expect(screen.getAllByRole('row', { name: /C-0002/ })).toHaveLength(1)
    expect(queryStateBlock('malformed-response')).toBeNull()
  })

  it('still fails closed when page 2 GENUINELY repeats a PartyId', async () => {
    // Same deferred timing, but this time the server really does send C-0001 again.
    const { release } = installDeferredSecondPage(page({ items: [party()] }))
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))
    await waitFor(() => expect(screen.getByRole('button', { name: /Yükleniyor/i })).toBeTruthy())
    expect(queryStateBlock('malformed-response')).toBeNull()

    release()
    // A broken continuation must not be de-duplicated into a quietly incomplete list.
    expect(await findStateBlock('malformed-response')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })
})

describe('C-01 reflow structure (browser UAT finding)', () => {
  /**
   * jsdom has no layout, so it cannot measure the overflow this pins. The layout proof
   * is the browser measurement recorded on the PR (320px: docScrollWidth === viewport,
   * nothing overflowing outside the scroll region). What these tests DO prevent is the
   * structure that made the overflow possible from coming back.
   */
  it('the table scrolls inside its own labelled, keyboard-reachable region', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    const region = screen.getByRole('region', { name: 'Cari listesi tablosu' })
    // Reachable by keyboard: without this the off-screen columns are mouse-only.
    expect(region.getAttribute('tabindex')).toBe('0')
    expect(region.contains(screen.getByRole('table'))).toBe(true)
  })

  it('the table keeps its table semantics — the scroll lives on the wrapper', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    // The previous attempt was `.table { display: block; overflow-x: auto }`, which
    // both failed to stop the page scrolling AND drops row/cell semantics from the
    // accessibility tree — costing a screen-reader user the column headers.
    const table = screen.getByRole('table')
    expect(table.tagName).toBe('TABLE')
    expect(screen.getAllByRole('columnheader')).toHaveLength(6)
    expect(screen.getAllByRole('row').length).toBeGreaterThan(1)
  })
})

describe('C-01 transport failure (ChatGPT #155 P1)', () => {
  /**
   * The shell test covers `/identity/me`. This one covers `src/cari/api.ts`, which the
   * same change touches — ChatGPT's review was right that mutating the cari branch
   * alone would have left every test green. The two clients classify independently,
   * so they need independent proof.
   */
  it('a counterparty request that never completes is a network error, not malformed', async () => {
    // identity / directory / capability all succeed, so the shell reaches C-01 and
    // ONLY the business read fails at the transport layer.
    installFetch()
    const shellFetch = globalThis.fetch as typeof fetch
    vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url.includes('/counterparties')) return Promise.reject(new TypeError('Failed to fetch'))
      return shellFetch(input, init)
    })

    renderAt('/cari')

    expect(await findStateBlock('network-error')).toBeTruthy()
    expect(queryStateBlock('malformed-response')).toBeNull()
    // the screen itself is still mounted; only the read failed
    expect(screen.getByRole('heading', { name: 'Cari Kartlar' })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Tekrar dene/i })).toBeTruthy()
  })

  it('a malformed counterparty BODY is still malformed — the two stay distinct', async () => {
    installFetch({ counterparties: () => json({ items: 'not-an-array' }) })
    renderAt('/cari')
    expect(await findStateBlock('malformed-response')).toBeTruthy()
    expect(queryStateBlock('network-error')).toBeNull()
  })
})

describe('C-01 continuation failure (B-C01-008)', () => {
  /** Page 1 succeeds; only the cursor request fails with the given status. */
  function installFailingSecondPage(status: number) {
    installFetch({
      counterparties: (url) =>
        url.includes('cursor=next-1')
          ? json({ code: 'service_unavailable', title: 'Servis yok', status }, status)
          : json(page({ hasMore: true, nextCursor: 'next-1' })),
    })
  }

  it.each([500, 503])(
    'a %i on the SECOND page keeps the first page visible instead of blanking the list',
    async (status) => {
      installFailingSecondPage(status)
      renderAt('/cari')
      await screen.findByRole('row', { name: /C-0001/ })

      await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))

      // The user asked for MORE. "More" failed. What they already had is still
      // valid — throwing it away punishes them for asking.
      await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
      expect(screen.getByRole('row', { name: /C-0001/ })).toBeTruthy()
      expect(queryStateBlock('service-unavailable')).toBeNull()
      // retry is offered at the CONTINUATION level, not as a whole-list reload
      expect(screen.getByRole('button', { name: /Tekrar dene/i })).toBeTruthy()
    },
  )

  it('a first-page failure still replaces the whole view — there is nothing to keep', async () => {
    installFetch({
      counterparties: () => json({ code: 'service_unavailable', title: 'x', status: 503 }, 503),
    })
    renderAt('/cari')
    expect(await findStateBlock('service-unavailable')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('retrying a failed continuation appends the page exactly once', async () => {
    let attempt = 0
    installFetch({
      counterparties: (url) => {
        if (!url.includes('cursor=next-1')) return json(page({ hasMore: true, nextCursor: 'next-1' }))
        attempt += 1
        return attempt === 1
          ? json({ code: 'service_unavailable', title: 'x', status: 503 }, 503)
          : json(page({ items: [party({ id: PARTY_2, code: 'C-0002' })] }))
      },
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))
    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: /Tekrar dene/i }))
    await waitFor(() => expect(screen.getByRole('row', { name: /C-0002/ })).toBeTruthy())
    expect(screen.getAllByRole('row', { name: /C-0001/ })).toHaveLength(1)
    expect(screen.getAllByRole('row', { name: /C-0002/ })).toHaveLength(1)
  })
})

describe('C-01 continuation failure — what must NOT be masked (Q8=A kapsam dışı)', () => {
  function installSecondPage(status: number, code: string) {
    installFetch({
      counterparties: (url) =>
        url.includes('cursor=next-1')
          ? json({ code, title: 'x', status }, status)
          : json(page({ hasMore: true, nextCursor: 'next-1' })),
    })
  }

  async function loadThenPage2(status: number, code: string) {
    installSecondPage(status, code)
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))
  }

  it('a 401 on the second page fails closed LOCALLY — rows are NOT preserved', async () => {
    await loadThenPage2(401, 'authentication_required')
    // Losing the session is not a paging hiccup. Keeping rows visible would show
    // data from a session the server has already rejected.
    //
    // SCOPE, not a gap: this asserts the LOCAL fail-closed render only. A
    // business 401 now does also end the session centrally — that is what #169
    // implements — but the central effect is the shell's to prove, and it is
    // proved in `shell/authBoundaryLiveActor.test.tsx` and
    // `cari/authBoundaryIntegration.test.tsx` against the real provider.
    // Asserting it here too would duplicate the guarantee in a file that cannot
    // see the reducer, which is how the earlier version of this comment ended up
    // documenting behaviour that had already changed.
    expect(await findStateBlock('unauthenticated')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText('C-0001')).toBeNull()
  })

  it('a 403 on the second page fails closed — rows are NOT preserved', async () => {
    await loadThenPage2(403, 'forbidden')
    // Authorization was revoked mid-session; continuing to show the list would
    // keep rendering records the actor is no longer entitled to.
    // The capability is re-fenced (SAB-004), so the gated route closes rather
    // than showing a local forbidden block over still-visible rows.
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText('C-0001')).toBeNull()
    expect(screen.queryByRole('link', { name: 'Cari Kartlar' })).toBeNull()
  })

  it('a malformed second page is NOT hidden behind "rows preserved"', async () => {
    installFetch({
      counterparties: (url) =>
        url.includes('cursor=next-1')
          ? json({ items: 'not-an-array' })
          : json(page({ hasMore: true, nextCursor: 'next-1' })),
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))

    // A contract violation must surface as one. "Keep the rows we have" is a
    // courtesy for transport failures, not a place to bury a broken server.
    expect(await findStateBlock('malformed-response')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('the continuation notice is announced, not just drawn', async () => {
    await loadThenPage2(503, 'service_unavailable')
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Daha fazla kayıt yüklenemedi')
    expect(alert.textContent).toContain('mevcut kayıtlar korunuyor')
  })
})

describe('C-01 continuation failure — the transport half of the allowlist', () => {
  /**
   * ChatGPT's #158 review: the allowlist has three entries but only
   * `service-unavailable` was exercised, so dropping `offline` or `network-error`
   * left every test green. Each entry needs its own proof.
   */
  async function loadPageOne() {
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))
  }

  it('offline on the second page keeps the validated rows', async () => {
    installFetch({
      counterparties: (url) => {
        if (!url.includes('cursor=next-1')) return json(page({ hasMore: true, nextCursor: 'next-1' }))
        throw new TypeError('Failed to fetch')
      },
    })
    const shellFetch = globalThis.fetch as typeof fetch
    vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url.includes('cursor=next-1')) {
        vi.stubGlobal('navigator', { onLine: false })
        return Promise.reject(new TypeError('Failed to fetch'))
      }
      return shellFetch(input, init)
    })

    await loadPageOne()
    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
    expect(screen.getByRole('row', { name: /C-0001/ })).toBeTruthy()
    expect(queryStateBlock('offline')).toBeNull()
  })

  it('network-error on the second page keeps the validated rows while ONLINE', async () => {
    installFetch({
      counterparties: () => json(page({ hasMore: true, nextCursor: "next-1" })),
    })
    const shellFetch = globalThis.fetch as typeof fetch
    vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      // navigator.onLine stays true: this is the DNS/CORS/refused case, not offline.
      if (url.includes('cursor=next-1')) return Promise.reject(new TypeError('Failed to fetch'))
      return shellFetch(input, init)
    })

    await loadPageOne()
    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
    expect(screen.getByRole('row', { name: /C-0001/ })).toBeTruthy()
    expect(queryStateBlock('network-error')).toBeNull()
  })
})

describe('non-disclosure: a denied route and an unknown route are indistinguishable', () => {
  /**
   * B-C01-002-UI, found against the real stack with an unauthorized actor.
   *
   * `/cari` without the capability rendered the neutral not-found block inside the
   * shell, while a genuinely unknown path fell through to react-router's own error
   * boundary — "Unexpected Application Error! 404 Not Found", no shell at all.
   *
   * Being able to tell those apart IS the disclosure. It answers the one question
   * non-disclosure exists to refuse: does this module exist and am I merely denied?
   */
  function blockHtmlAt(path: string) {
    const el = document.querySelector('[data-state]')
    return { path, kind: el?.getAttribute('data-state') ?? null, html: el?.outerHTML ?? null }
  }

  it('both render the same block, inside the same shell', async () => {
    installFetch({ capabilities: [] })
    renderAt('/cari')
    await findStateBlock('not-found')
    const denied = blockHtmlAt('/cari')
    const deniedHadShell = document.querySelector('nav') !== null
    cleanup()

    installFetch({ capabilities: [] })
    renderAt('/boyle-bir-sayfa-yok')
    await findStateBlock('not-found')
    const unknown = blockHtmlAt('/boyle-bir-sayfa-yok')

    expect(denied.kind).toBe('not-found')
    expect(unknown.kind).toBe('not-found')
    expect(unknown.html).toBe(denied.html)
    expect(deniedHadShell).toBe(true)
    expect(document.querySelector('nav')).not.toBeNull()
  })

  it('an unknown route does not reach react-router error boundary', async () => {
    installFetch({ capabilities: [] })
    renderAt('/hic-olmayan/derin/yol')
    await findStateBlock('not-found')
    expect(document.body.textContent).not.toContain('Unexpected Application Error')
  })
})

describe('C-01 → C-03 navigation (spec §2.1)', () => {
  it('without the detail grant the code is TEXT, not a link', () => {
    // Holding the list does not imply holding the detail (#182). Rendering a link
    // the route would refuse offers an action the caller is not entitled to and
    // tells them the screen exists. Codex caught this on #172 (P1-1).
    //
    // Written because making the link unconditional broke no other test: the
    // conditional was unproven, which in an authorization path is the same as
    // absent.
    return (async () => {
      installFetch({ capabilities: [CAPABILITY.cariPartiesList] })
      renderAt('/cari')
      const row = await screen.findByRole('row', { name: /C-0001/ })
      expect(within(row).getByText('C-0001')).toBeTruthy()
      expect(within(row).queryByRole('link')).toBeNull()
    })()
  })

  it('the code opens that counterparty, and names it for screen readers', async () => {
    installFetch()
    renderAt('/cari')
    const row = await screen.findByRole('row', { name: /C-0001/ })

    const link = within(row).getByRole('link')
    expect(link.getAttribute('href')).toBe(`/cari/${PARTY_1}`)
    // Heard out of context, "C-0001" alone says nothing about which record it is.
    expect(link.getAttribute('aria-label')).toBe('C-0001 — Alfa Metal Ltd. Şti.')
  })

  it('every row links to its OWN record, not a shared or first id', async () => {
    installFetch({
      counterparties: () =>
        json(page({ items: [party(), party({ id: PARTY_2, code: 'C-0002' })] })),
    })
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0002/ })

    const hrefs = [...document.querySelectorAll('tbody a')].map((a) => a.getAttribute('href'))
    expect(hrefs).toEqual([`/cari/${PARTY_1}`, `/cari/${PARTY_2}`])
    expect(new Set(hrefs).size).toBe(2)
  })

  it('without the capability there is no link, because there is no list', async () => {
    installFetch({ capabilities: [] })
    renderAt('/cari')
    await findStateBlock('not-found')
    expect(document.querySelectorAll('tbody a')).toHaveLength(0)
  })
})

describe('C-01 the view survives a visit to a record (UX-CARI-001 #1)', () => {
  /** The C-03 shape: `version` is an opaque decimal STRING here, unlike the list. */
  function detailBody() {
    return {
      id: PARTY_1,
      code: 'C-0001',
      kind: 1,
      legalName: 'Alfa Metal Ltd. Şti.',
      tradeName: 'Alfa',
      isActive: true,
      version: '1',
      companyAccounts: [
        {
          id: ACC_1,
          companyId: A_ID,
          roles: PARTY_ROLE_FLAG.Customer,
          isActive: true,
          version: '1',
        },
      ],
    }
  }

  /** List calls only — the detail read hits the same prefix with the id in the path. */
  function listCalls(calls: readonly { url: string; companyId: string | null }[]) {
    return calls.filter((c) => c.url.includes('/counterparties') && !c.url.includes(PARTY_1))
  }

  it('restores the search and filters, and RE-READS the rows rather than restoring them', async () => {
    const { calls } = installFetch({
      counterparties: (url) => (url.includes(PARTY_1) ? json(detailBody()) : json(page())),
    })
    const router = renderRouterAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.type(screen.getByRole('searchbox'), 'alfa')
    await userEvent.selectOptions(screen.getByLabelText('Şirket hesabı durumu'), 'inactive')
    await waitFor(() =>
      expect(
        listCalls(calls).some(
          (c) => c.url.includes('q=alfa') && c.url.includes('accountState=inactive'),
        ),
      ).toBe(true),
    )

    // Open the record the way a user does, from the row.
    const row = await screen.findByRole('row', { name: /C-0001/ })
    await userEvent.click(within(row).getByRole('link'))
    await waitFor(() => expect(router.state.location.pathname).toBe(`/cari/${PARTY_1}`))
    expect(screen.queryByRole('searchbox')).toBeNull()

    const before = listCalls(calls).length
    await act(async () => {
      await router.navigate(-1)
    })

    // Back on the list: what the user asked for is still what the screen is asking.
    const searchbox = await screen.findByRole('searchbox', undefined, { timeout: 5000 })
    expect((searchbox as HTMLInputElement).value).toBe('alfa')
    expect((screen.getByLabelText('Şirket hesabı durumu') as HTMLSelectElement).value).toBe(
      'inactive',
    )

    // And the ROWS were fetched again. This is the half that keeps the entry gate
    // meaningful (#146/#148): a revisit re-authorizes and re-reads, so remembering
    // the query cannot become a way to repaint records read under an older grant.
    await waitFor(() => expect(listCalls(calls).length).toBeGreaterThan(before))
    const latest = listCalls(calls).at(-1)
    expect(latest?.url).toContain('q=alfa')
    expect(latest?.url).toContain('accountState=inactive')
    expect(latest?.companyId).toBe(A_ID)
  })

  it('restores the sort as well, so the order is not silently reset', async () => {
    const { calls } = installFetch({
      counterparties: (url) => (url.includes(PARTY_1) ? json(detailBody()) : json(page())),
    })
    const router = renderRouterAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    const codeHeader = screen.getByRole('columnheader', { name: /Kod/ })
    await userEvent.click(within(codeHeader).getByRole('button'))
    await waitFor(() => expect(codeHeader.getAttribute('aria-sort')).toBe('descending'))

    await userEvent.click(within(await screen.findByRole('row', { name: /C-0001/ })).getByRole('link'))
    await waitFor(() => expect(router.state.location.pathname).toBe(`/cari/${PARTY_1}`))
    const before = listCalls(calls).length
    await act(async () => {
      await router.navigate(-1)
    })

    await screen.findByRole('searchbox', undefined, { timeout: 5000 })
    expect(
      screen.getByRole('columnheader', { name: /Kod/ }).getAttribute('aria-sort'),
    ).toBe('descending')
    await waitFor(() => expect(listCalls(calls).length).toBeGreaterThan(before))
    expect(listCalls(calls).at(-1)?.url).toContain('direction=desc')
  })
})

describe('C-01 what narrowed the list, and the way out (UX-CARI-001 #2)', () => {
  it('shows no summary and no clear control on an untouched screen', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    // A control that would do nothing is worse than absent: it implies state exists.
    expect(screen.queryByRole('button', { name: /Filtreleri temizle/i })).toBeNull()
    expect(screen.queryByText(/Etkin filtreler/i)).toBeNull()
  })

  it('names each active filter, so an empty list explains itself', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.type(screen.getByRole('searchbox'), 'alfa')
    await userEvent.click(screen.getByText('Geçmiş sınıflandırma'))
    await userEvent.click(screen.getByRole('checkbox', { name: 'Müşteri' }))
    await userEvent.selectOptions(screen.getByLabelText('Taraf durumu'), 'inactive')

    const summary = await screen.findByText(/Etkin filtreler/i)
    const row = summary.parentElement as HTMLElement
    expect(within(row).getByText('Arama: “alfa”')).toBeTruthy()
    // Named as history: the chip is often the only place the applied role filter
    // is visible at all.
    expect(within(row).getByText('Geçmiş rol: Müşteri')).toBeTruthy()
    expect(within(row).getByText('Taraf durumu: Pasif')).toBeTruthy()
    // The account-state filter is untouched, so naming it would make the default
    // look like something the user chose.
    expect(within(row).queryByText(/Şirket hesabı durumu/)).toBeNull()
  })

  it('clears every filter in one action and asks the server again without them', async () => {
    const { calls } = installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.type(screen.getByRole('searchbox'), 'alfa')
    await userEvent.click(screen.getByRole('checkbox', { name: 'Müşteri' }))
    await userEvent.selectOptions(screen.getByLabelText('Şirket hesabı durumu'), 'inactive')
    await userEvent.selectOptions(screen.getByLabelText('Taraf durumu'), 'all')

    await userEvent.click(screen.getByRole('button', { name: /Filtreleri temizle/i }))

    expect((screen.getByRole('searchbox') as HTMLInputElement).value).toBe('')
    expect((screen.getByRole('checkbox', { name: 'Müşteri' }) as HTMLInputElement).checked).toBe(
      false,
    )
    expect((screen.getByLabelText('Şirket hesabı durumu') as HTMLSelectElement).value).toBe('all')
    expect((screen.getByLabelText('Taraf durumu') as HTMLSelectElement).value).toBe('active')
    expect(screen.queryByRole('button', { name: /Filtreleri temizle/i })).toBeNull()

    await waitFor(() => {
      const latest = calls.filter((c) => c.url.includes('/counterparties')).at(-1)
      expect(latest?.url).not.toContain('q=alfa')
      expect(latest?.url).not.toContain('role=Customer')
      expect(latest?.url).toContain('accountState=all')
      expect(latest?.url).toContain('partyState=active')
    })
  })

  it('clearing filters leaves the chosen sort alone — order is not a filter', async () => {
    const { calls } = installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    const codeHeader = screen.getByRole('columnheader', { name: /Kod/ })
    await userEvent.click(within(codeHeader).getByRole('button'))
    await waitFor(() => expect(codeHeader.getAttribute('aria-sort')).toBe('descending'))
    await userEvent.type(screen.getByRole('searchbox'), 'alfa')

    await userEvent.click(screen.getByRole('button', { name: /Filtreleri temizle/i }))

    expect(codeHeader.getAttribute('aria-sort')).toBe('descending')
    await waitFor(() =>
      expect(calls.filter((c) => c.url.includes('/counterparties')).at(-1)?.url).toContain(
        'direction=desc',
      ),
    )
  })

  it('clearing moves focus to the search box instead of dropping it on <body>', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })
    await userEvent.type(screen.getByRole('searchbox'), 'alfa')

    await userEvent.click(screen.getByRole('button', { name: /Filtreleri temizle/i }))

    // The button unmounts with the last filter it cleared. Without an explicit move
    // a keyboard user is returned to the top of the document with no idea where.
    expect(document.activeElement).toBe(screen.getByRole('searchbox'))
  })

  it('an empty result still being replaced is not labelled "no records"', async () => {
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    let unfiltered = 0
    installFetch({
      counterparties: (url) => {
        if (url.includes('q=zzz')) return json(page({ items: [] }))
        unfiltered += 1
        // First unfiltered read: the company genuinely has nothing yet.
        if (unfiltered === 1) return json(page({ items: [] }))
        // The read issued by "clear filters" — held open so its in-flight window
        // is observable.
        return gate.then(() => json(page())) as unknown as Response
      },
    })
    renderAt('/cari')
    expect(await findStateBlock('empty-no-data')).toBeTruthy()

    await userEvent.type(screen.getByRole('searchbox'), 'zzz')
    await waitFor(() => expect(queryStateBlock('empty-filtered')).not.toBeNull())

    await userEvent.click(screen.getByRole('button', { name: /Filtreleri temizle/i }))

    // `keepPreviousData` still holds the FILTERED empty page. Labelling it by the
    // filters now on screen said "Kayıt yok" over a company that has records.
    await waitFor(() => expect(queryStateBlock('initial-loading')).not.toBeNull())
    expect(queryStateBlock('empty-no-data')).toBeNull()
    expect(queryStateBlock('empty-filtered')).toBeNull()

    release()
    await waitFor(() => expect(screen.getByRole('row', { name: /C-0001/ })).toBeTruthy())
  })
})

describe('C-01 the role filter is a LEGACY surface (ILK_DILIM §4.1, §7)', () => {
  /** The C-03 shape: `version` is an opaque decimal STRING here, unlike the list. */
  function detailBody() {
    return {
      id: PARTY_1,
      code: 'C-0001',
      kind: 1,
      legalName: 'Alfa Metal Ltd. Şti.',
      tradeName: 'Alfa',
      isActive: true,
      version: '1',
      companyAccounts: [
        { id: ACC_1, companyId: A_ID, roles: PARTY_ROLE_FLAG.Customer, isActive: true, version: '1' },
      ],
    }
  }

  /**
   * Addressed through its own summary. `<details>` has no stable mapped role across
   * the versions in play, and guessing one would make this suite assert about the
   * accessibility mapping rather than about the behaviour it is here to pin.
   */
  function legacyBlock() {
    return screen.getByText('Geçmiş sınıflandırma').closest('details') as HTMLDetailsElement
  }

  it('is collapsed by default, so it is not offered as the way to find a counterparty', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    // The decision removed the mandatory commercial role. A role filter sitting in
    // the first row of controls, equal to search, teaches the operator that the
    // classification is still how you find a customer — the exact precondition
    // that was removed.
    expect(legacyBlock().open).toBe(false)
  })

  it('says the markers are historical rather than the current work direction', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.click(screen.getByText('Geçmiş sınıflandırma'))
    expect(legacyBlock().open).toBe(true)
    expect(within(legacyBlock()).getByText(/eski kayıtlardan gelir/i)).toBeTruthy()
    // And it warns about the one effect that is easy to miss: an applied role
    // filter drops every record that carries no classification at all.
    expect(within(legacyBlock()).getByText(/ticari rolü olmayan cariler/i)).toBeTruthy()
  })

  it('names an applied role filter as a PAST classification in the summary', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.click(screen.getByText('Geçmiş sınıflandırma'))
    await userEvent.click(screen.getByRole('checkbox', { name: 'Müşteri' }))

    const summary = await screen.findByText(/Etkin filtreler/i)
    expect(within(summary.parentElement as HTMLElement).getByText('Geçmiş rol: Müşteri')).toBeTruthy()
  })

  it('re-opens itself when a restored view carries a role filter', async () => {
    installFetch({
      counterparties: (url) => (url.includes(PARTY_1) ? json(detailBody()) : json(page())),
    })
    const router = renderRouterAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.click(screen.getByText('Geçmiş sınıflandırma'))
    await userEvent.click(screen.getByRole('checkbox', { name: 'Müşteri' }))

    await userEvent.click(
      within(await screen.findByRole('row', { name: /C-0001/ })).getByRole('link'),
    )
    await waitFor(() => expect(router.state.location.pathname).toBe(`/cari/${PARTY_1}`))
    await act(async () => {
      await router.navigate(-1)
    })
    await screen.findByRole('searchbox', undefined, { timeout: 5000 })

    // A filter that is APPLIED but folded away is the §4.1 failure exactly: the
    // list looks like every counterparty while it is silently missing the ones
    // that carry no classification. Restoring the query has to restore its
    // visibility with it.
    expect(legacyBlock().open).toBe(true)
    expect((screen.getByRole('checkbox', { name: 'Müşteri' }) as HTMLInputElement).checked).toBe(
      true,
    )
  })

  it('is collapsed again after the filters are cleared', async () => {
    installFetch()
    renderAt('/cari')
    await screen.findByRole('row', { name: /C-0001/ })

    await userEvent.click(screen.getByText('Geçmiş sınıflandırma'))
    await userEvent.click(screen.getByRole('checkbox', { name: 'Müşteri' }))
    await userEvent.click(screen.getByRole('button', { name: /Filtreleri temizle/i }))

    expect((screen.getByRole('checkbox', { name: 'Müşteri' }) as HTMLInputElement).checked).toBe(
      false,
    )
    expect(legacyBlock().open).toBe(false)
  })
})

describe('C-01 a counterparty with no commercial role (ILK_DILIM §7)', () => {
  it('is an ordinary row — it does not reject the page it arrives in', async () => {
    installFetch({
      counterparties: () =>
        json(
          page({
            items: [
              party({
                companyAccounts: [
                  { id: ACC_1, companyId: A_ID, roles: 0, isActive: true, version: 1 },
                ],
              }),
            ],
          }),
        ),
    })
    renderAt('/cari')

    // Before this, one role-less record made the WHOLE page malformed: the operator
    // saw an error block instead of a list.
    const row = await screen.findByRole('row', { name: /C-0001/ })
    expect(queryStateBlock('malformed-response')).toBeNull()
    expect(within(row).getByText('Ticari rol yok')).toBeTruthy()
  })

  it('says the absence rather than leaving the cell blank', async () => {
    installFetch({
      counterparties: () =>
        json(
          page({
            items: [
              party({
                companyAccounts: [
                  { id: ACC_1, companyId: A_ID, roles: 0, isActive: true, version: 1 },
                ],
              }),
            ],
          }),
        ),
    })
    renderAt('/cari')

    const row = await screen.findByRole('row', { name: /C-0001/ })
    const cell = within(row).getByText('Ticari rol yok')
    expect(cell.textContent?.trim()).not.toBe('')
    // …and it is not dressed up as a defect. The record is complete.
    expect(row.textContent).not.toMatch(/eksik|belirtilmemiş|sınıflandırılmamış|yetkisiz/i)
    // The state columns are untouched by it: no role is not passive.
    expect(within(row).getAllByText('Aktif').length).toBe(2)
  })

  it('a role-less account does not become passive or unauthorized', async () => {
    installFetch({
      counterparties: () =>
        json(
          page({
            items: [
              party({
                isActive: true,
                companyAccounts: [
                  { id: ACC_1, companyId: A_ID, roles: 0, isActive: true, version: 1 },
                ],
              }),
            ],
          }),
        ),
    })
    renderAt('/cari')

    const row = await screen.findByRole('row', { name: /C-0001/ })
    expect(row.getAttribute('data-inactive')).toBeNull()
    // The link is still offered: the detail grant is what decides that, never a
    // commercial classification.
    expect(within(row).getByRole('link')).toBeTruthy()
  })

  it('unknown role bits are STILL malformed — one bound moved, not the rest', async () => {
    installFetch({
      counterparties: () =>
        json(
          page({
            items: [
              party({
                companyAccounts: [
                  { id: ACC_1, companyId: A_ID, roles: 16, isActive: true, version: 1 },
                ],
              }),
            ],
          }),
        ),
    })
    renderAt('/cari')

    expect(await findStateBlock('malformed-response')).toBeTruthy()
  })
})

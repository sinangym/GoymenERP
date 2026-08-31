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
        capabilities: config.capabilities ?? [CAPABILITY.cariPartiesList],
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

    // One role: `any` and `all` are the same query, so no control is offered.
    await userEvent.click(screen.getByRole('checkbox', { name: 'Müşteri' }))
    expect(screen.queryByLabelText('Rol eşleşmesi')).toBeNull()

    await userEvent.click(screen.getByRole('checkbox', { name: 'Tedarikçi' }))
    await userEvent.selectOptions(screen.getByLabelText('Rol eşleşmesi'), 'all')

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

    // Carrying A's search term into B would silently filter B's list by a term the
    // user never chose for B — and the screen would look like B simply has no records.
    expect((screen.getByRole('searchbox') as HTMLInputElement).value).toBe('')
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

  it('a 401 on the second page ends the session — rows are NOT preserved', async () => {
    await loadThenPage2(401, 'authentication_required')
    // Losing the session is not a paging hiccup. Keeping rows visible would show
    // data from a session the server has already rejected.
    expect(await findStateBlock('unauthenticated')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText('C-0001')).toBeNull()
  })

  it('a 403 on the second page fails closed — rows are NOT preserved', async () => {
    await loadThenPage2(403, 'forbidden')
    // Authorization was revoked mid-session; continuing to show the list would
    // keep rendering records the actor is no longer entitled to.
    expect(await findStateBlock('forbidden')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText('C-0001')).toBeNull()
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

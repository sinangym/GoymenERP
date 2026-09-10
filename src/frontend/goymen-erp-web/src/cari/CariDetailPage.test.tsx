import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
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
const PARTY = '11111111-1111-1111-1111-111111111111'
const ACC = 'cccccccc-1111-1111-1111-111111111111'

const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Kullanıcı A',
  email: null,
  allowedCompanyIds: [A_ID, B_ID],
}
const DIRECTORY = [
  { id: A_ID, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: B_ID, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function detail(over: Record<string, unknown> = {}) {
  return {
    id: PARTY,
    code: 'CAR-000001',
    kind: 1,
    legalName: 'Örnek Metal Sanayi ve Ticaret A.Ş.',
    tradeName: 'Örnek Metal',
    isActive: true,
    version: '3',
    companyAccounts: [
      {
        id: ACC,
        companyId: A_ID,
        roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier,
        isActive: true,
        version: '2',
      },
    ],
    ...over,
  }
}

interface Config {
  capabilities?: readonly string[]
  detail?: (companyId: string | null, url: string) => Response
}

function installFetch(config: Config = {}) {
  const calls: { url: string; companyId: string | null }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const companyId =
        (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
      calls.push({ url, companyId })
      if (url.includes('/identity/me')) return json(IDENTITY)
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities'))
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: `gen-${companyId?.slice(-1) ?? 'x'}`,
          // The detail is a separate grant (#182). The default here holds BOTH because
          // these cases are about the screen's content; the grant matrix is exercised
          // explicitly in the capability tests below.
          capabilities:
            config.capabilities ?? [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail],
        })
      if (/\/counterparties\/[^?]+/.test(url))
        return (config.detail ?? (() => json(detail())))(companyId, url)
      if (url.includes('/counterparties'))
        return json({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null })
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  return { calls }
}

function renderAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return { queryClient, router }
}

/**
 * This screen resolves through four SEQUENTIAL round-trips — identity, directory,
 * capability, then the detail itself. Testing Library's 1s default is not a
 * generous budget for that under parallel test load, and the flake it produced
 * was a timeout, not a wrong assertion. Raising the ceiling does not weaken any
 * check; it stops the suite teaching everyone to re-run until green.
 */
const CHAIN = { timeout: 5000 } as const

function factValue(label: string): string | null {
  const dt = [...document.querySelectorAll('dt')].find((n) => n.textContent === label)
  return dt?.nextElementSibling?.textContent ?? null
}

async function findStateBlock(kind: string): Promise<HTMLElement> {
  return await waitFor(() => {
    const node = document.querySelector<HTMLElement>(`[data-state="${kind}"]`)
    if (node === null) throw new Error(`state block "${kind}" not rendered`)
    return node
  }, CHAIN)
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

describe('C-03 core summary (spec §5.1)', () => {
  it('renders exactly the accepted fields from the real contract shape', async () => {
    const { calls } = installFetch()
    renderAt(`/cari/${PARTY}`)

    expect(await screen.findByRole('heading', { name: /Örnek Metal Sanayi/ }, CHAIN)).toBeTruthy()
    expect(factValue('Cari kodu')).toBe('CAR-000001')
    expect(factValue('Hukuki unvan')).toBe('Örnek Metal Sanayi ve Ticaret A.Ş.')
    expect(factValue('Ticari unvan')).toBe('Örnek Metal')
    expect(factValue('Tür')).toBe('Tüzel kişi')
    expect(factValue('Geçmiş ticari sınıflandırma')).toBe('Müşteri, Tedarikçi')

    // The company is named from the shell DIRECTORY, not from the record — the
    // response carries only an id. That is a second query, so it can resolve after
    // the detail does; until it has, the field honestly shows a dash rather than
    // guessing a name from the id.
    await waitFor(() => {
      expect(factValue('Aktif şirket')).toBe('Göymen Demir Çelik (GDC)')
    }, CHAIN)

    const call = calls.find((c) => /\/counterparties\/[^?]+/.test(c.url))
    expect(call?.companyId).toBe(A_ID)
  })

  it('keeps the global Party state and the company account state separate', async () => {
    installFetch({
      detail: () =>
        json(
          detail({
            isActive: true,
            companyAccounts: [
              { id: ACC, companyId: A_ID, roles: PARTY_ROLE_FLAG.Customer, isActive: false, version: '2' },
            ],
          }),
        ),
    })
    renderAt(`/cari/${PARTY}`)
    await screen.findByRole('heading', { name: /Örnek Metal/ }, CHAIN)
    expect(factValue('Taraf durumu')).toBe('Aktif')
    expect(factValue('Şirket hesabı durumu')).toBe('Pasif')
  })

  it('shows no balance, risk, limit, stock or order field (mockTruthForbidden)', async () => {
    installFetch()
    renderAt(`/cari/${PARTY}`)
    await screen.findByRole('heading', { name: /Örnek Metal/ }, CHAIN)
    const text = document.body.textContent ?? ''
    for (const forbidden of ['Bakiye', 'Risk', 'Limit', 'Stok', 'Sipariş', 'Açık kalem', 'Teminat']) {
      expect(text).not.toContain(forbidden)
    }
    // and no empty placeholder pretending a value exists but is zero
    expect(text).not.toMatch(/\b0,00\b/)
  })

  it('omits the trade name when it adds nothing', async () => {
    installFetch({ detail: () => json(detail({ tradeName: null })) })
    renderAt(`/cari/${PARTY}`)
    await screen.findByRole('heading', { name: /Örnek Metal/ }, CHAIN)
    expect(factValue('Ticari unvan')).toBeNull()
  })
})

describe('C-03 non-disclosure (spec §6)', () => {
  it('a 404 reveals nothing — not the id, not a partial name, not a heading', async () => {
    installFetch({ detail: () => json({ code: 'not_found', title: 'Bulunamadı' }, 404) })
    renderAt(`/cari/${PARTY}`)

    expect(await findStateBlock('not-found')).toBeTruthy()
    const text = document.body.textContent ?? ''
    // The heading must stay generic: naming the record would confirm it exists.
    expect(screen.getByRole('heading', { name: 'Cari 360' })).toBeTruthy()
    expect(text).not.toContain(PARTY)
    expect(text).not.toContain('Örnek Metal')
  })

  it('a foreign-company account rejects the WHOLE record, not just a field', async () => {
    installFetch({
      detail: () =>
        json(
          detail({
            companyAccounts: [
              { id: ACC, companyId: B_ID, roles: PARTY_ROLE_FLAG.Customer, isActive: true, version: '2' },
            ],
          }),
        ),
    })
    renderAt(`/cari/${PARTY}`)
    expect(await findStateBlock('malformed-response')).toBeTruthy()
    // Showing the legal name while rejecting its account would leak the record.
    expect(document.body.textContent).not.toContain('Örnek Metal')
  })

  it('without the capability, a deep link mounts nothing and calls no API', async () => {
    const { calls } = installFetch({ capabilities: [] })
    renderAt(`/cari/${PARTY}`)
    await findStateBlock('not-found')
    expect(screen.queryByRole('heading', { name: 'Cari 360' })).toBeNull()
    expect(calls.some((c) => /\/counterparties\//.test(c.url))).toBe(false)
  })
})

describe('C-03 context binding (spec §6)', () => {
  it('a company switch does not carry the old Cari name into the new context', async () => {
    installFetch({
      detail: (companyId) =>
        companyId === A_ID
          ? json(detail())
          : json({ code: 'not_found', title: 'Bulunamadı' }, 404),
    })
    renderAt(`/cari/${PARTY}`)
    await screen.findByRole('heading', { name: /Örnek Metal/ }, CHAIN)

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)

    // The name lives in the heading here, so a stale render would put one
    // company's counterparty under another company's label.
    await findStateBlock('not-found')
    expect(document.body.textContent).not.toContain('Örnek Metal')
    expect(screen.getByRole('heading', { name: 'Cari 360' })).toBeTruthy()
  })

  it('offers a way back to the list from every state', async () => {
    installFetch({ detail: () => json({ code: 'not_found' }, 404) })
    renderAt(`/cari/${PARTY}`)
    await findStateBlock('not-found')
    const back = screen.getByRole('link', { name: /listesine dön/ })
    expect(back.getAttribute('href')).toBe('/cari')
  })
})

describe('C-03 accessibility', () => {
  it('one heading, definition list semantics, reachable back link', async () => {
    installFetch()
    renderAt(`/cari/${PARTY}`)
    await screen.findByRole('heading', { name: /Örnek Metal/ }, CHAIN)

    expect(document.querySelectorAll('h1')).toHaveLength(1)
    const list = document.querySelector('dl')
    expect(list).not.toBeNull()
    expect(within(list as HTMLElement).getAllByRole('term').length).toBeGreaterThan(4)
    expect(screen.getByRole('link', { name: /listesine dön/ })).toBeTruthy()
  })

  it('a passive state is not signalled by colour alone', async () => {
    installFetch({
      detail: () =>
        json(
          detail({
            isActive: false,
            companyAccounts: [
              { id: ACC, companyId: A_ID, roles: PARTY_ROLE_FLAG.Customer, isActive: false, version: '2' },
            ],
          }),
        ),
    })
    renderAt(`/cari/${PARTY}`)
    await screen.findByRole('heading', { name: /Örnek Metal/ }, CHAIN)
    expect(factValue('Taraf durumu')).toBe('Pasif')
    expect(factValue('Şirket hesabı durumu')).toBe('Pasif')
  })
})

describe('C-03 capability matrix — the detail is a SEPARATE grant (#182)', () => {
  // Codex required this matrix on #172: holding the list must not imply holding
  // the detail, and the reserved static paths must not be swallowed by the param
  // route. Each case asserts the REQUEST COUNT as well as the render, because
  // "did not display it" and "did not ask for it" are different guarantees and
  // only the second one is authorization.

  it('list-only: the detail route closes and no detail read is issued', async () => {
    const { calls } = installFetch({ capabilities: [CAPABILITY.cariPartiesList] })
    renderAt(`/cari/${PARTY}`)

    expect(await findStateBlock('not-found')).toBeTruthy()
    expect(screen.queryByRole('heading', { name: /Cari 360/i })).toBeNull()
    expect(
      calls.some((c) => c.url.includes(`/counterparties/${PARTY}`)),
      'a detail read was issued without the detail capability',
    ).toBe(false)
  })

  it('detail-only: the detail opens even without the list grant', async () => {
    // The two grants are independent in both directions. If the detail silently
    // required the list, the frontend would be narrowing a grant the server made.
    installFetch({ capabilities: [CAPABILITY.cariPartiesDetail] })
    renderAt(`/cari/${PARTY}`)

    expect(await screen.findByRole('heading', { name: /Cari 360/i })).toBeTruthy()
  })

  it('both: the detail opens', async () => {
    installFetch({
      capabilities: [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail],
    })
    renderAt(`/cari/${PARTY}`)
    expect(await screen.findByRole('heading', { name: /Cari 360/i })).toBeTruthy()
  })

  it('none: the detail route closes non-disclosingly and asks for nothing', async () => {
    const { calls } = installFetch({ capabilities: [] })
    renderAt(`/cari/${PARTY}`)

    expect(await findStateBlock('not-found')).toBeTruthy()
    expect(calls.some((c) => c.url.includes('/counterparties'))).toBe(false)
  })

  it('reserved static paths are not read as a party id', async () => {
    // `/cari/yeni` and `/cari/sirkete-bagla` are declared by the server and not
    // implemented here. Without a reserved route ahead of `:partyId`, the detail
    // screen mounts and issues a read for a record that cannot exist.
    for (const path of ['/cari/yeni', '/cari/sirkete-bagla']) {
      cleanup()
      const { calls } = installFetch({
        capabilities: [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail],
      })
      renderAt(path)

      expect(await findStateBlock('not-found'), `${path} did not close`).toBeTruthy()
      expect(
        calls.some((c) => c.url.includes('/counterparties/')),
        `${path} was read as a party id`,
      ).toBe(false)
    }
  })
})

describe('C-03 a counterparty with no commercial role (ILK_DILIM §7)', () => {
  it('renders the record and states that the field is not required', async () => {
    installFetch({
      detail: () =>
        json(
          detail({
            companyAccounts: [
              { id: ACC, companyId: A_ID, roles: 0, isActive: true, version: '2' },
            ],
          }),
        ),
    })
    renderAt(`/cari/${PARTY}`)

    // The heading proves the whole record resolved: before the decoder accepted 0,
    // a role-less counterparty was `malformed` and this screen showed an error.
    await screen.findByRole('heading', { name: /Örnek Metal Sanayi/ })
    expect(factValue('Geçmiş ticari sınıflandırma')).toBe('Ticari rol yok — bu alan zorunlu değil')
  })

  it('does not present the absence as a gap in the record', async () => {
    installFetch({
      detail: () =>
        json(
          detail({
            companyAccounts: [
              { id: ACC, companyId: A_ID, roles: 0, isActive: true, version: '2' },
            ],
          }),
        ),
    })
    renderAt(`/cari/${PARTY}`)
    await screen.findByRole('heading', { name: /Örnek Metal Sanayi/ })

    const facts = document.querySelector('dl')?.textContent ?? ''
    expect(facts).not.toMatch(/eksik|belirtilmemiş|sınıflandırılmamış|yetkisiz/i)
    // The two state facts are unchanged by it — no role is not passive.
    expect(factValue('Taraf durumu')).toBe('Aktif')
    expect(factValue('Şirket hesabı durumu')).toBe('Aktif')
  })

  it('still fails closed on an unknown role bit', async () => {
    installFetch({
      detail: () =>
        json(
          detail({
            companyAccounts: [
              { id: ACC, companyId: A_ID, roles: 16, isActive: true, version: '2' },
            ],
          }),
        ),
    })
    renderAt(`/cari/${PARTY}`)

    expect(await findStateBlock('malformed-response')).toBeTruthy()
  })
})

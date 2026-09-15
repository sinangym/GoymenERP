/**
 * C-03 company relationships panel (spec §14.2) through the REAL shell, router, reducer
 * and query client — only `fetch` is stubbed. The server behaviour the stub imitates is
 * pinned on the backend by `CounterpartyCompanyRelationshipsReadAcceptanceTests`: without
 * `X-Company-Id` the detail read returns one account per company the actor may read in;
 * with it, exactly that company's account.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { CARI_RELATIONSHIPS_TEXT as TEXT } from './cariCommandText'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const C_ID = 'cccccccc-0000-0000-0000-000000000003'
const PARTY = '11111111-1111-1111-1111-111111111111'
const ACC_A = 'aaaaaaaa-1111-1111-1111-111111111111'
const ACC_B = 'bbbbbbbb-1111-1111-1111-111111111111'

const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Kullanıcı A',
  email: null,
  allowedCompanyIds: [A_ID, B_ID],
}
const COMPANY_A = { id: A_ID, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false }
const COMPANY_B = { id: B_ID, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false }
const COMPANY_C = { id: C_ID, code: 'GIN', legalName: 'Göymen İnşaat', isPlaceholder: true }
const B_LABEL = 'Göymen Sac Profil (GSP)'

const CHAIN = { timeout: 5000 } as const

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const ACCOUNT_A = { id: ACC_A, companyId: A_ID, roles: 1, isActive: true, version: '2' }
const ACCOUNT_B = { id: ACC_B, companyId: B_ID, roles: 0, isActive: false, version: '5' }

function party(accounts: unknown[]) {
  return {
    id: PARTY,
    code: 'CAR-000001',
    kind: 1,
    legalName: 'Örnek Metal Sanayi ve Ticaret A.Ş.',
    tradeName: null,
    isActive: true,
    version: '3',
    companyAccounts: accounts,
  }
}

interface Config {
  /** A function when a case changes the directory the shell reads next. */
  directory?: readonly unknown[] | (() => readonly unknown[])
  /** The group read (no company header). */
  group?: () => Response | Promise<Response>
}

interface Call {
  readonly url: string
  readonly companyId: string | null
}

function installFetch(config: Config = {}) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const headers = (init?.headers as Record<string, string> | undefined) ?? {}
      const companyId = headers['X-Company-Id'] ?? null
      calls.push({ url, companyId })
      if (url.includes('/identity/me')) return json(IDENTITY)
      if (url.includes('/organization/companies')) {
        const directory = typeof config.directory === 'function' ? config.directory() : config.directory
        return json(directory ?? [COMPANY_A, COMPANY_B])
      }
      if (url.includes('/shell/capabilities'))
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: `gen-${companyId?.slice(-1) ?? 'x'}`,
          capabilities: [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail],
        })
      if (url.includes(`/counterparties/${PARTY}`)) {
        // The company-bound card read: exactly the requested company's account.
        if (companyId === A_ID) return json(party([ACCOUNT_A]))
        if (companyId === B_ID) return json(party([ACCOUNT_B]))
        return (config.group ?? (() => json(party([ACCOUNT_A, ACCOUNT_B]))))()
      }
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  const partyCalls = () => calls.filter((c) => c.url.includes(`/counterparties/${PARTY}`))
  return { calls, partyCalls, groupCalls: () => partyCalls().filter((c) => c.companyId === null) }
}

function renderAt(path: string, options: { keepCache?: boolean } = {}) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  // `keepCache` uses the library's default garbage-collection window, as the app does, so a
  // case can prove an answer cached in one context is never painted in another.
  const queryClient = new QueryClient({
    defaultOptions: { queries: options.keepCache ? { retry: false } : { retry: false, gcTime: 0 } },
  })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return { queryClient, router }
}

async function openCard() {
  await screen.findByRole('heading', { name: /Örnek Metal Sanayi/ }, CHAIN)
}

function panel(): HTMLElement {
  const node = document.querySelector<HTMLElement>('[data-section="company-relationships"]')
  if (node === null) throw new Error('relationships panel not rendered')
  return node
}

async function openPanel() {
  await openCard()
  const toggle = await screen.findByRole('button', { name: TEXT.show }, CHAIN)
  await userEvent.click(toggle)
}

function rows(): HTMLElement[] {
  return [...panel().querySelectorAll<HTMLElement>('tbody tr')]
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
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('C-03 company relationships (spec §14.2)', () => {
  it('is closed until asked for: opening the card sends one read, bound to the active company', async () => {
    const fetches = installFetch()
    renderAt(`/cari/${PARTY}`)
    await openCard()
    const toggle = await screen.findByRole('button', { name: TEXT.show }, CHAIN)
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(fetches.partyCalls()).toEqual([{ url: expect.stringContaining(PARTY), companyId: A_ID }])
    expect(fetches.groupCalls()).toHaveLength(0)
    expect(panel().querySelector('table')).toBeNull()
  })

  it('lists every authorized company account with its own state and classification', async () => {
    const fetches = installFetch()
    renderAt(`/cari/${PARTY}`)
    await openPanel()

    await waitFor(() => expect(rows()).toHaveLength(2), CHAIN)
    const group = fetches.groupCalls()
    expect(group).toHaveLength(1)
    // No company header and no company query parameter: the scope is the server's.
    expect(group[0].url).not.toContain('companyId')

    const [a, b] = rows()
    expect(a.dataset.companyId).toBe(A_ID)
    expect(a.dataset.current).toBe('yes')
    expect(within(a).getByRole('rowheader').textContent).toBe('Göymen Demir Çelik (GDC)')
    expect(within(a).getByText('Aktif')).toBeTruthy()
    expect(within(a).getByText('Müşteri')).toBeTruthy()
    expect(within(a).getByText(TEXT.current)).toBeTruthy()
    expect(within(a).queryByRole('button')).toBeNull()

    expect(b.dataset.companyId).toBe(B_ID)
    expect(b.dataset.current).toBe('no')
    expect(within(b).getByRole('rowheader').textContent).toBe(B_LABEL)
    // B's passive account is B's fact. The card's own account section (A) is untouched.
    expect(b.querySelector('[data-account-state]')?.getAttribute('data-account-state')).toBe('inactive')
    expect(within(b).getByText('Ticari rol yok')).toBeTruthy()
    expect(within(b).getByRole('button', { name: TEXT.openIn(B_LABEL) })).toBeTruthy()
    expect(
      document.querySelector('[data-section="account"] [data-account-state]')?.getAttribute('data-account-state'),
    ).toBe('active')

    expect(panel().querySelector('[data-state="only-this-company"]')).toBeNull()
    // No figure stands in for Finance's amounts, and no internal id is printed.
    expect(panel().textContent).not.toMatch(/bakiye|tutar|risk|limit|₺|TRY/i)
    expect(panel().textContent).not.toContain(ACC_B)
  })

  it('opens the card in another company through a verified shell switch', async () => {
    const fetches = installFetch()
    renderAt(`/cari/${PARTY}`)
    await openPanel()
    const open = await within(panel()).findByRole('button', { name: TEXT.openIn(B_LABEL) }, CHAIN)
    await userEvent.click(open)

    // The picker commits to B, and the card re-reads company-bound in B.
    await waitFor(() => {
      const picker = screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement
      expect(picker.disabled).toBe(false)
      expect(picker.value).toBe(B_ID)
    }, CHAIN)
    await waitFor(() => expect(fetches.partyCalls().some((c) => c.companyId === B_ID)).toBe(true), CHAIN)
    await waitFor(() => {
      expect(
        document.querySelector('[data-section="account"] [data-account-state]')?.getAttribute('data-account-state'),
      ).toBe('inactive')
    }, CHAIN)
    // A new context is a new card: the panel starts closed again, and no second group read went out.
    expect(await screen.findByRole('button', { name: TEXT.show }, CHAIN)).toBeTruthy()
    expect(fetches.groupCalls()).toHaveLength(1)
  })

  it('says so when the active company is the only one with an account', async () => {
    installFetch({ group: () => json(party([ACCOUNT_A])) })
    renderAt(`/cari/${PARTY}`)
    await openPanel()
    expect(await within(panel()).findByText(TEXT.onlyThisCompany, {}, CHAIN)).toBeTruthy()
    expect(rows()).toHaveLength(1)
  })

  it.each([
    ['an account under a company the shell does not list', () =>
      json(party([ACCOUNT_A, { ...ACCOUNT_B, companyId: C_ID }]))],
    ['no account for the active company', () => json(party([ACCOUNT_B]))],
    ['a numeric version', () => json(party([{ ...ACCOUNT_A, version: 2 }, ACCOUNT_B]))],
  ])('refuses the whole answer for %s, and the card above stays', async (_label, group) => {
    installFetch({ group })
    renderAt(`/cari/${PARTY}`)
    await openPanel()
    await waitFor(() => expect(panel().querySelector('[data-state="malformed-response"]')).toBeTruthy(), CHAIN)
    expect(panel().querySelector('table')).toBeNull()
    expect(panel().textContent).not.toContain(C_ID)
    expect(screen.getByRole('heading', { name: /Örnek Metal Sanayi/ })).toBeTruthy()
  })

  it('keeps a not-found and a forbidden group answer inside the panel, without revoking the card', async () => {
    for (const status of [404, 403]) {
      const fetches = installFetch({ group: () => json({ code: 'x' }, status) })
      const view = renderAt(`/cari/${PARTY}`)
      await openPanel()
      const kind = status === 404 ? 'not-found' : 'forbidden'
      await waitFor(() => expect(panel().querySelector(`[data-state="${kind}"]`)).toBeTruthy(), CHAIN)
      // The card is still there and its context was not re-verified because of the group read.
      expect(screen.getByRole('heading', { name: /Örnek Metal Sanayi/ })).toBeTruthy()
      const capabilityReads = fetches.calls.filter((c) => c.url.includes('/shell/capabilities')).length
      await new Promise((resolve) => setTimeout(resolve, 50))
      expect(fetches.calls.filter((c) => c.url.includes('/shell/capabilities')).length).toBe(capabilityReads)
      view.router.dispose()
      cleanup()
      vi.unstubAllGlobals()
      vi.stubGlobal('navigator', { onLine: true })
    }
  })

  it('shows the card’s client-contract refusal, not a record conflict, and re-sends nothing', async () => {
    for (const code of ['client_contract_unsupported', 'client_contract_required']) {
      const fetches = installFetch({ group: () => json({ code }, 409) })
      const view = renderAt(`/cari/${PARTY}`)
      await openPanel()

      const refusal = await waitFor(() => {
        const node = panel().querySelector<HTMLElement>('[data-state="client-contract"]')
        expect(node, code).toBeTruthy()
        return node as HTMLElement
      }, CHAIN)
      // The card's wording and meaning: this client is not compatible; the record did not change.
      expect(refusal.textContent).toContain('Uyumlu istemci gerekli')
      expect(refusal.textContent).toContain('Sayfayı yenileyin')
      expect(panel().querySelector('[data-state="conflict"]')).toBeNull()
      expect(panel().textContent).not.toContain('Çakışma')
      // The card's recovery too: a reloaded client, never a second send of this one.
      expect(within(panel()).queryByRole('button', { name: 'Tekrar dene' })).toBeNull()

      const capabilityReads = fetches.calls.filter((c) => c.url.includes('/shell/capabilities')).length
      await new Promise((resolve) => setTimeout(resolve, 100))
      expect(fetches.groupCalls()).toHaveLength(1)
      // Not reported to the auth boundary, and the card above is untouched.
      expect(fetches.calls.filter((c) => c.url.includes('/shell/capabilities')).length).toBe(capabilityReads)
      expect(screen.getByRole('heading', { name: /Örnek Metal Sanayi/ })).toBeTruthy()
      expect(
        document.querySelector('[data-section="account"] [data-account-state]')?.getAttribute('data-account-state'),
      ).toBe('active')

      view.router.dispose()
      cleanup()
      vi.unstubAllGlobals()
      vi.stubGlobal('navigator', { onLine: true })
    }
  })

  it('never shows a late group answer under the company switched to meanwhile', async () => {
    let releaseA: (response: Response) => void = () => {}
    const pendingA = new Promise<Response>((resolve) => {
      releaseA = resolve
    })
    let groupCall = 0
    const fetches = installFetch({
      group: () => {
        groupCall += 1
        // A's question is held; B's own question never answers, so B has nothing of its own.
        return groupCall === 1 ? pendingA : new Promise<Response>(() => {})
      },
    })
    renderAt(`/cari/${PARTY}`, { keepCache: true })
    await openPanel()
    await waitFor(() => expect(fetches.groupCalls()).toHaveLength(1), CHAIN)

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)
    await waitFor(() => {
      expect(
        document.querySelector('[data-section="account"] [data-account-state]')?.getAttribute('data-account-state'),
      ).toBe('inactive')
    }, CHAIN)
    // B's panel is OPEN while A's answer is still out: the one place a late answer could paint.
    await userEvent.click(await screen.findByRole('button', { name: TEXT.show }, CHAIN))

    releaseA(json(party([ACCOUNT_A, ACCOUNT_B])))
    await new Promise((resolve) => setTimeout(resolve, 150))
    expect(panel().querySelector('table')).toBeNull()
    expect(panel().querySelector('[data-state="initial-loading"]')).toBeTruthy()
    // B asked its own question; A's answer was never taken for it.
    expect(fetches.groupCalls()).toHaveLength(2)
  })

  it('never paints a group answer cached in one company context inside another', async () => {
    let calls = 0
    const fetches = installFetch({
      group: () => {
        calls += 1
        // The first context's answer lands; the second context's read stays in flight.
        return calls === 1 ? json(party([ACCOUNT_A, ACCOUNT_B])) : new Promise<Response>(() => {})
      },
    })
    renderAt(`/cari/${PARTY}`, { keepCache: true })
    await openPanel()
    await waitFor(() => expect(rows()).toHaveLength(2), CHAIN)

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)
    await waitFor(() => {
      expect(
        document.querySelector('[data-section="account"] [data-account-state]')?.getAttribute('data-account-state'),
      ).toBe('inactive')
    }, CHAIN)
    await userEvent.click(await screen.findByRole('button', { name: TEXT.show }, CHAIN))
    await waitFor(() => expect(fetches.groupCalls()).toHaveLength(2), CHAIN)

    // B asked its own question; until B's answer lands there is nothing to show.
    expect(panel().querySelector('[data-state="initial-loading"]')).toBeTruthy()
    expect(panel().querySelector('table')).toBeNull()
  })

  it('keeps the toggle’s aria-controls target in the document while the panel is closed', async () => {
    installFetch()
    renderAt(`/cari/${PARTY}`)
    await openCard()
    const toggle = await screen.findByRole('button', { name: TEXT.show }, CHAIN)
    const targetId = toggle.getAttribute('aria-controls') ?? ''

    const closed = document.getElementById(targetId)
    expect(closed).toBeTruthy()
    expect(closed?.hidden).toBe(true)
    expect(panel().contains(closed)).toBe(true)

    await userEvent.click(toggle)
    const opened = document.getElementById(targetId)
    expect(opened?.hidden).toBe(false)
    expect(opened?.textContent).toContain(TEXT.note)
  })

  it('offers no switch into a company the refreshed directory no longer lists', async () => {
    let directory: readonly unknown[] = [COMPANY_A, COMPANY_B, COMPANY_C]
    const fetches = installFetch({ directory: () => directory })
    const view = renderAt(`/cari/${PARTY}`)
    await openPanel()
    await within(panel()).findByRole('button', { name: TEXT.openIn(B_LABEL) }, CHAIN)

    // The shell reads its directory again while the panel is open, and B is no longer in it.
    directory = [COMPANY_A, COMPANY_C]
    const directoryReads = fetches.calls.filter((c) => c.url.includes('/organization/companies')).length
    await act(async () => {
      await view.queryClient.invalidateQueries({ queryKey: ['shell', 'directory'] })
    })
    await waitFor(() => {
      expect(fetches.calls.filter((c) => c.url.includes('/organization/companies')).length).toBeGreaterThan(
        directoryReads,
      )
      expect(within(panel()).queryAllByRole('button', { name: /şirketinde aç$/ })).toHaveLength(0)
    }, CHAIN)
    // Nothing else was asked of the server on the directory's behalf.
    expect(fetches.groupCalls()).toHaveLength(1)
  })

  it('is not offered to an actor who sees a single company', async () => {
    installFetch({ directory: [COMPANY_A] })
    renderAt(`/cari/${PARTY}`)
    await openCard()
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(document.querySelector('[data-section="company-relationships"]')).toBeNull()
  })
})

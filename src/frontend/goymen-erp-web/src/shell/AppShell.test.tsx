import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const USER_A = '11111111-1111-1111-1111-111111111111'
const USER_B = '22222222-2222-2222-2222-222222222222'

function identity(userId = USER_A) {
  return {
    userId,
    displayName: userId === USER_A ? 'Kullanıcı A' : 'Kullanıcı B',
    email: 'test@example.com',
    allowedCompanyIds: [A_ID, B_ID],
  }
}
const DIRECTORY = [
  { id: A_ID, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true },
  { id: B_ID, code: 'GSP', legalName: 'Göymen B', isPlaceholder: true },
]
function capability(gen: string) {
  return { contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: gen, capabilities: [] }
}

type Responder = (companyId: string | null) => Promise<Response> | Response

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function installFetch(config: {
  identity?: Responder
  directory?: Responder
  capabilities?: Responder
}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const companyId =
      (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
    if (url.includes('/identity/me')) return (config.identity ?? (() => json(identity())))(companyId)
    if (url.includes('/organization/companies'))
      return (config.directory ?? (() => json(DIRECTORY)))(companyId)
    if (url.includes('/shell/capabilities'))
      return (config.capabilities ??
        (() => json(capability(companyId === B_ID ? 'gen-b-1' : 'gen-a-1'))))(companyId)
    throw new Error(`unexpected fetch: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderShell() {
  const router = createMemoryRouter(shellRoutes, { initialEntries: ['/'] })
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

describe('SHELL-PILOT-01 bootstrap', () => {
  it('resolves identity + directory + capabilities and shows the active company', async () => {
    installFetch({})
    renderShell()
    expect(await screen.findByText('Ortak kabuk hazır')).toBeTruthy()
    expect(screen.getByText('Göymen A')).toBeTruthy()
    expect(screen.getByText('Kullanıcı A')).toBeTruthy()
    expect(screen.getByText(/henüz uygulanmış modül yok/i)).toBeTruthy()
  })

  it('shows "no authorized company" when the directory is empty', async () => {
    installFetch({ directory: () => json([]) })
    renderShell()
    expect(await screen.findByText(/Yetkili şirket yok/i)).toBeTruthy()
  })

  it('P1-2: a 503 capability does NOT commit a context — retryable failure, no shell', async () => {
    installFetch({ capabilities: () => json({ title: 'x' }, 503) })
    renderShell()
    expect(await screen.findByText(/şirket bağlamı kurulamadı/i)).toBeTruthy()
    expect(screen.queryByText('Ortak kabuk hazır')).toBeNull()
    expect(screen.getByRole('button', { name: /Tekrar dene/i })).toBeTruthy()
  })

  it('P1-3: a capability snapshot with the wrong contract version fails closed', async () => {
    installFetch({
      capabilities: () => json({ contractVersion: 'v1', generation: 'g', capabilities: [] }),
    })
    renderShell()
    expect(await screen.findByText(/şirket bağlamı kurulamadı/i)).toBeTruthy()
    expect(screen.queryByText('Ortak kabuk hazır')).toBeNull()
  })

  it('P1-3: an empty capability generation fails closed', async () => {
    installFetch({ capabilities: () => json(capability('   ')) })
    renderShell()
    expect(await screen.findByText(/şirket bağlamı kurulamadı/i)).toBeTruthy()
  })

  it('P1-2: a 401 capability shows "session ended" and does not loop or show a base shell', async () => {
    const fetchMock = installFetch({ capabilities: () => json({ title: 'unauth' }, 401) })
    renderShell()
    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()
    expect(screen.queryByText('Ortak kabuk hazır')).toBeNull()
    const callsAfter = fetchMock.mock.calls.length
    // no bootstrap retry loop
    await new Promise((r) => setTimeout(r, 50))
    expect(fetchMock.mock.calls.length).toBe(callsAfter)
  })

  it('fails closed on a malformed directory response', async () => {
    installFetch({ directory: () => json({ not: 'an array' }) })
    renderShell()
    expect(await screen.findByText(/Beklenmeyen yanıt/i)).toBeTruthy()
  })
})

describe('SHELL-PILOT-01 P1-1: actor change clears the old context', () => {
  it('when identity switches A -> B, SESSION_RESET fires and the old company/cache is cleared', async () => {
    let currentUser = USER_A
    const fetchMock = installFetch({ identity: () => json(identity(currentUser)) })
    const queryClient = renderShell()
    await screen.findByText('Ortak kabuk hazır')
    expect(screen.getByText('Kullanıcı A')).toBeTruthy()
    const clearSpy = vi.spyOn(queryClient, 'clear')

    // The authenticated actor changes under the same SPA session; refetch identity.
    currentUser = USER_B
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })

    await waitFor(() => {
      // never actor A's name alongside a committed company
      expect(screen.queryByText('Kullanıcı A')).toBeNull()
    })
    expect(clearSpy).toHaveBeenCalled()
    expect(fetchMock).toHaveBeenCalled()
  })

  it('Codex #139 P1-1: actor B actually gets a working shell (not a permanent dead end)', async () => {
    let currentUser = USER_A
    installFetch({ identity: () => json(identity(currentUser)) })
    const queryClient = renderShell()
    await screen.findByText('Ortak kabuk hazır')
    expect(screen.getByText('Kullanıcı A')).toBeTruthy()

    currentUser = USER_B
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })

    // The old bug: SESSION_RESET kept committedContextRevision, so bootstrap never
    // re-ran and actor B was stuck with no company forever.
    expect(await screen.findByText('Kullanıcı B')).toBeTruthy()
    expect(await screen.findByText('Ortak kabuk hazır')).toBeTruthy()
    expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(A_ID)
  })
})

describe('SHELL-PILOT-01 company switch', () => {
  it('out-of-order: a slow old-company response after switching does not change the committed company', async () => {
    let releaseA: () => void = () => {}
    const gateA = new Promise<void>((resolve) => {
      releaseA = resolve
    })
    let capCalls = 0
    installFetch({
      capabilities: async (companyId) => {
        capCalls += 1
        if (companyId === A_ID && capCalls > 1) {
          await gateA
          return json(capability('gen-a-2'))
        }
        return json(capability(companyId === B_ID ? 'gen-b-1' : 'gen-a-1'))
      },
    })
    renderShell()
    await screen.findByText('Ortak kabuk hazır')
    const select = screen.getByRole('combobox') as HTMLSelectElement

    await userEvent.selectOptions(select, B_ID)
    await waitFor(() => expect(select.value).toBe(B_ID))

    await userEvent.selectOptions(select, A_ID)
    releaseA()
    await waitFor(() => expect(select.value).toBe(A_ID))
    expect(screen.getByText('Göymen A')).toBeTruthy()
  })

  it('P1-2: a 5xx during switch keeps the former company active and shows a retry', async () => {
    installFetch({
      capabilities: (companyId) =>
        companyId === B_ID ? json({ title: 'x' }, 502) : json(capability('gen-a-1')),
    })
    renderShell()
    await screen.findByText('Ortak kabuk hazır')
    const select = screen.getByRole('combobox') as HTMLSelectElement

    await userEvent.selectOptions(select, B_ID)
    await waitFor(() => expect(screen.getByText(/Şirket değiştirilemedi/i)).toBeTruthy())
    // former company A is still the committed context
    expect(select.value).toBe(A_ID)
    expect(screen.getByText('Göymen A')).toBeTruthy()
  })

  it.each([
    ['403', () => json({ title: 'forbidden' }, 403)],
    ['404', () => json({ title: 'not found' }, 404)],
    ['malformed body', () => json({ contractVersion: 'nope', generation: '', capabilities: [] })],
    ['202 with a valid body (P1-2: only 200 is success)', () => json(capability('gen-b-1'), 202)],
  ] as const)('a failed switch (%s) rolls back to the former company', async (_label, responder) => {
    installFetch({
      capabilities: (companyId) => (companyId === B_ID ? responder() : json(capability('gen-a-1'))),
    })
    renderShell()
    await screen.findByText('Ortak kabuk hazır')
    const select = screen.getByRole('combobox') as HTMLSelectElement

    await userEvent.selectOptions(select, B_ID)
    await waitFor(() => expect(select.value).toBe(A_ID))
    expect(screen.getByText('Göymen A')).toBeTruthy()
    expect(screen.getByText('Ortak kabuk hazır')).toBeTruthy()
  })

  it('the company picker is disabled while a switch is in flight (no picker-driven race)', async () => {
    let releaseB: () => void = () => {}
    const gateB = new Promise<void>((resolve) => {
      releaseB = resolve
    })
    installFetch({
      capabilities: async (companyId) => {
        if (companyId === B_ID) {
          await gateB
          return json(capability('gen-b-1'))
        }
        return json(capability('gen-a-1'))
      },
    })
    renderShell()
    await screen.findByText('Ortak kabuk hazır')
    const select = screen.getByRole('combobox') as HTMLSelectElement

    await userEvent.selectOptions(select, B_ID)
    // While 'switching' the picker must not accept another target — this is the
    // UI-level half of the fence; the reducer's forGeneration check is the other half.
    await waitFor(() => expect(select.disabled).toBe(true))

    releaseB()
    await waitFor(() => expect(select.disabled).toBe(false))
    expect(select.value).toBe(B_ID)
  })

  it('a superseded in-flight capability response cannot paint after the fence moved', async () => {
    let releaseB: () => void = () => {}
    const gateB = new Promise<void>((resolve) => {
      releaseB = resolve
    })
    let currentUser = USER_A
    installFetch({
      identity: () => json(identity(currentUser)),
      capabilities: async (companyId) => {
        if (companyId === B_ID) {
          // B's reauth stalls; a session reset will supersede it while in flight.
          await gateB
          return json(capability('gen-b-late'))
        }
        return json(capability('gen-a-1'))
      },
    })
    const queryClient = renderShell()
    await screen.findByText('Ortak kabuk hazır')
    const select = screen.getByRole('combobox') as HTMLSelectElement

    await userEvent.selectOptions(select, B_ID)
    await waitFor(() => expect(select.disabled).toBe(true))

    // Reachable supersede: the authenticated actor changes while B is still in flight.
    // SESSION_RESET advances the fence, so B's attempt generation is now stale.
    currentUser = USER_B
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await waitFor(() => expect(screen.getByText('Kullanıcı B')).toBeTruthy())

    // The superseded B response lands now. It must not commit company B for actor B.
    releaseB()
    await new Promise((r) => setTimeout(r, 30))
    expect(screen.getByText('Kullanıcı B')).toBeTruthy()
    // The rendered active company is the source of truth: actor B bootstrapped to its
    // own preferred company (A), never the stale in-flight B attempt.
    expect(await screen.findByText('Göymen A')).toBeTruthy()
    expect(screen.queryByText('Göymen B')).toBeNull()
  })

  it('ERP-000K §13.7: with no bound context the picker shows no stale company', async () => {
    // Capability never resolves -> no committed context -> the picker must not display
    // a company as if it were active.
    installFetch({ capabilities: () => json({ title: 'x' }, 503) })
    renderShell()
    await screen.findByText(/şirket bağlamı kurulamadı/i)
    expect(screen.queryByRole('combobox')).toBeNull()
  })
})

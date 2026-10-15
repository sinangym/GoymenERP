import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { useShellOrchestrator } from './useShellOrchestrator'
import { useShellState } from './shellContext'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const USER_A = '11111111-1111-1111-1111-111111111111'
const USER_B = '22222222-2222-2222-2222-222222222222'
/**
 * Controlled server session bindings (M01.3 r5, #66/6187473245): opaque values the stubbed `/identity/me` answers with,
 * one per authenticated server session. The shell takes them as given and mints none.
 */
const SESSION_A1 = `v1.${'a1'.repeat(32)}`
const SESSION_A2 = `v1.${'a2'.repeat(32)}`
const SESSION_B1 = `v1.${'b1'.repeat(32)}`

function identity(userId = USER_A, authenticationSessionBinding: string | null = userId === USER_A ? SESSION_A1 : SESSION_B1) {
  return {
    userId,
    displayName: userId === USER_A ? 'Kullanıcı A' : 'Kullanıcı B',
    email: 'test@example.com',
    allowedCompanyIds: [A_ID, B_ID],
    authenticationSessionBinding,
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

interface RenderFrame {
  readonly actor: string | null
  readonly ctxActor: string | null
  readonly mismatch: boolean
}

/**
 * Records EVERY render, not just the final DOM. The actor-change hole lives in a single
 * intermediate frame; a post-hoc `screen` query runs after all effects have flushed and
 * can never observe it. Owns the orchestrator effects (it is the only caller here).
 */
function ActorProbe({ log }: { readonly log: RenderFrame[] }) {
  const { identity, actorMismatch } = useShellOrchestrator()
  const state = useShellState()
  log.push({
    actor: identity?.userId ?? null,
    ctxActor: state.activeContext?.userId ?? null,
    mismatch: actorMismatch,
  })
  return null
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

  /**
   * Found running the merged shell against the REAL API. The fetch itself failed
   * (CORS, in that case) and the shell reported "Sunucu yanıtı beklenen biçimde
   * değil" — blaming the server's response body for a request that never produced
   * one. It cost me a debugging round, and it would cost anyone else the same.
   *
   * The browser deliberately hides WHY a fetch failed, so we cannot narrow it to
   * CORS vs DNS vs refused. What we can distinguish, and must, is "no response
   * arrived" from "a response arrived and was wrong".
   */
  it('a request that never completes is a network error, NOT a malformed response', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))),
    )
    renderShell()

    expect(await screen.findByText(/Sunucuya ulaşılamadı/i)).toBeTruthy()
    expect(screen.queryByText(/Beklenmeyen yanıt/i)).toBeNull()
    // ...and it is retryable, like the other transport states.
    expect(screen.getByRole('button', { name: /Tekrar dene/i })).toBeTruthy()
  })

  it('still calls a bad BODY malformed — the two states stay distinct', async () => {
    installFetch({ identity: () => json({ userId: 42 }) })
    renderShell()
    expect(await screen.findByText(/Beklenmeyen yanıt/i)).toBeTruthy()
    expect(screen.queryByText(/Sunucuya ulaşılamadı/i)).toBeNull()
  })

  it('Codex #139 P1-3: a 401 during bootstrap clears the business cache too', async () => {
    installFetch({ capabilities: () => json({ title: 'unauth' }, 401) })
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: 0 } },
    })
    // A business read cached under the dying session — nothing the shell hook knows about.
    queryClient.setQueryData(['cari', 'counterparties'], { items: [{ code: 'C-0001' }] })
    const router = createMemoryRouter(shellRoutes, { initialEntries: ['/'] })
    render(
      <ShellProvider queryClient={queryClient}>
        <RouterProvider router={router} />
      </ShellProvider>,
    )

    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()
    // The session is dead; a read taken under it must not survive for the next actor.
    await waitFor(() => {
      expect(queryClient.getQueryData(['cari', 'counterparties'])).toBeUndefined()
    })
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

  it('Codex #139 P1-1: no render pairs the new actor with the old context unfenced', async () => {
    let currentUser = USER_A
    installFetch({ identity: () => json(identity(currentUser)) })
    const log: RenderFrame[] = []
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: 0 } },
    })
    render(
      <ShellProvider queryClient={queryClient}>
        <ActorProbe log={log} />
      </ShellProvider>,
    )
    await waitFor(() => expect(log.some((f) => f.ctxActor === USER_A)).toBe(true))

    currentUser = USER_B
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await waitFor(() =>
      expect(log.some((f) => f.actor === USER_B && f.ctxActor === USER_B)).toBe(true),
    )

    // The dangerous frame must actually have happened — otherwise this test proves
    // nothing and would stay green even with the gate deleted.
    expect(log.some((f) => f.actor !== null && f.ctxActor !== null && f.actor !== f.ctxActor)).toBe(
      true,
    )

    // ...and every such frame must be flagged, so the shell can fail closed inside it
    // rather than one paint later, when the reset effect finally runs.
    const unfenced = log.filter(
      (f) => f.actor !== null && f.ctxActor !== null && f.actor !== f.ctxActor && !f.mismatch,
    )
    expect(unfenced).toEqual([])
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

    // The rendered active company is the source of truth: actor B bootstraps to the
    // last COMMITTED company (A), never the stale in-flight B attempt. The superseded
    // attempt must not have leaked into the stored preference either.
    await waitFor(() => expect(screen.getByText('Göymen A')).toBeTruthy())
    expect(screen.getByText('Kullanıcı B')).toBeTruthy()
    expect(screen.queryByText('Göymen B')).toBeNull()
    expect(window.localStorage.getItem('goymen-company-id')).toBe(A_ID)
  })

  it('the stored company preference tracks the committed company', async () => {
    installFetch({})
    renderShell()
    await screen.findByText('Ortak kabuk hazır')
    // REV.4 derives the preference from the COMMITTED context, so the write happens in
    // an effect AFTER the paint that says the shell is ready. Reading it synchronously
    // races that effect — it passed locally and failed on the slower CI runner.
    await waitFor(() => expect(window.localStorage.getItem('goymen-company-id')).toBe(A_ID))

    await userEvent.selectOptions(screen.getByRole('combobox'), B_ID)
    await waitFor(() => expect(window.localStorage.getItem('goymen-company-id')).toBe(B_ID))
    expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(B_ID)
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

/*
 * M01.3 r5 (#66/6187473245): the session binding is the SERVER's `authenticationSessionBinding` from `/identity/me`,
 * carried into SESSION_READY from the same response as the actor. The shell mints nothing, stores nothing of its own, and
 * reads a same-actor change of the value as a real session boundary — while an ordinary F5 or a token-refresh-equivalent
 * re-read that answers the same value is no boundary at all.
 */
describe('M01.3 server session binding', () => {
  interface Seen {
    readonly epoch: number
    readonly binding: string | null
    readonly actor: string | null
  }

  function BindingProbe({ seen }: { readonly seen: Seen[] }) {
    const state = useShellState()
    seen.push({ epoch: state.sessionEpoch, binding: state.sessionBinding, actor: state.activeContext?.userId ?? null })
    return null
  }

  function mount(seen: Seen[]) {
    const router = createMemoryRouter(shellRoutes, { initialEntries: ['/'] })
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
    const view = render(
      <ShellProvider queryClient={queryClient}>
        <BindingProbe seen={seen} />
        <RouterProvider router={router} />
      </ShellProvider>,
    )
    return { queryClient, view }
  }

  const last = (seen: readonly Seen[]) => seen[seen.length - 1]
  const UUID_SHAPED = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

  beforeEach(() => {
    sessionStorage.clear()
  })

  it('takes the binding from the identity response and keeps it, at epoch 0, over an F5 that the server still answers the same way', async () => {
    const fetchMock = installFetch({})
    const seen: Seen[] = []
    const first = mount(seen)
    await screen.findByText('Ortak kabuk hazır')
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A1, epoch: 0 }))
    // Nothing of the shell's own in the tab: the server value is the only session identity.
    expect(Object.keys(sessionStorage)).toEqual([])

    first.view.unmount()
    const after: Seen[] = []
    mount(after)
    await screen.findByText('Ortak kabuk hazır')
    await waitFor(() => expect(last(after)).toMatchObject({ actor: USER_A, binding: SESSION_A1, epoch: 0 }))
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/identity/me'))).toHaveLength(2)
  })

  it('a token-refresh-equivalent identity re-read with the same actor and binding is no boundary', async () => {
    const fetchMock = installFetch({})
    const seen: Seen[] = []
    const { queryClient } = mount(seen)
    await screen.findByText('Ortak kabuk hazır')
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A1 }))
    const capabilityReads = () => fetchMock.mock.calls.filter(([url]) => String(url).includes('/shell/capabilities')).length
    const bootstraps = capabilityReads()

    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/identity/me'))).toHaveLength(2))

    expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A1, epoch: 0 })
    expect(capabilityReads()).toBe(bootstraps)
    expect(seen.some((frame) => frame.epoch > 0)).toBe(false)
  })

  it('the same actor with a different server binding is a real session boundary: the epoch advances, the old binding never pairs with the new session', async () => {
    let session = SESSION_A1
    installFetch({ identity: () => json(identity(USER_A, session)) })
    const seen: Seen[] = []
    const { queryClient } = mount(seen)
    await screen.findByText('Ortak kabuk hazır')
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A1, epoch: 0 }))

    // A real logout/login as the same actor: the server answers a new binding.
    session = SESSION_A2
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A2 }))
    expect(last(seen).epoch).toBe(1)
    // The boundary dropped the old binding in the same transition that advanced the epoch; no frame pairs them.
    expect(seen.some((frame) => frame.epoch >= 1 && frame.binding === SESSION_A1)).toBe(false)
    expect(seen.some((frame) => frame.epoch === 0 && frame.binding === SESSION_A2)).toBe(false)
  })

  it('A→B→A, each answered as its own server session, carries each binding only with its own actor and session', async () => {
    let current = identity(USER_A, SESSION_A1)
    installFetch({ identity: () => json(current) })
    const seen: Seen[] = []
    const { queryClient } = mount(seen)
    await screen.findByText('Kullanıcı A')
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A1, epoch: 0 }))

    current = identity(USER_B, SESSION_B1)
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await screen.findByText('Kullanıcı B')
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_B, binding: SESSION_B1, epoch: 1 }))

    current = identity(USER_A, SESSION_A2)
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await screen.findByText('Kullanıcı A')
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A2, epoch: 2 }))

    for (const frame of seen) {
      if (frame.binding === null) continue
      const owner = frame.binding === SESSION_B1 ? USER_B : USER_A
      expect(frame.actor === null || frame.actor === owner).toBe(true)
    }
    expect(seen.some((frame) => frame.epoch === 2 && frame.binding === SESSION_A1)).toBe(false)
  })

  it('an explicit null binding is a valid fail-closed answer: the shell is ready, the binding is null and no local stand-in is made', async () => {
    installFetch({ identity: () => json(identity(USER_A, null)) })
    const seen: Seen[] = []
    mount(seen)
    await screen.findByText('Ortak kabuk hazır')
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_A, epoch: 0 }))
    expect(last(seen).binding).toBeNull()
    expect(seen.every((frame) => frame.binding === null)).toBe(true)
    expect(Object.keys(sessionStorage)).toEqual([])
    // ...and not a client UUID, not the epoch, not the actor id.
    expect(seen.some((frame) => frame.binding !== null && (UUID_SHAPED.test(frame.binding) || frame.binding === USER_A))).toBe(false)
  })

  it.each([
    ['missing', () => { const { authenticationSessionBinding: _omitted, ...rest } = identity(); return rest }],
    ['a number', () => ({ ...identity(), authenticationSessionBinding: 42 })],
    ['an empty string', () => ({ ...identity(), authenticationSessionBinding: '' })],
    ['whitespace only', () => ({ ...identity(), authenticationSessionBinding: '   ' })],
    ['an object', () => ({ ...identity(), authenticationSessionBinding: { v: 1 } })],
  ])('an identity whose binding is %s is malformed: no context is committed and nothing is read as null', async (_case, body) => {
    const fetchMock = installFetch({ identity: () => json(body()) })
    const seen: Seen[] = []
    mount(seen)
    expect(await screen.findByText(/Beklenmeyen yanıt/i)).toBeTruthy()
    expect(screen.queryByText('Ortak kabuk hazır')).toBeNull()
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/shell/capabilities'))).toHaveLength(0)
    expect(seen.every((frame) => frame.actor === null && frame.binding === null && frame.epoch === 0)).toBe(true)
  })

  it('a stale identity answer cannot commit its binding after the session moved on', async () => {
    // The first bootstrap's capability read hangs until released; meanwhile the server re-authenticates the same actor.
    let release: (() => void) | null = null
    let held = true
    let session = SESSION_A1
    installFetch({
      identity: () => json(identity(USER_A, session)),
      capabilities: (companyId) => {
        if (held) {
          held = false
          return new Promise<Response>((resolve) => {
            release = () => resolve(json(capability(companyId === B_ID ? 'gen-b-1' : 'gen-a-1')))
          })
        }
        return json(capability(companyId === B_ID ? 'gen-b-1' : 'gen-a-1'))
      },
    })
    const seen: Seen[] = []
    const { queryClient } = mount(seen)
    await waitFor(() => expect(release).not.toBeNull())

    session = SESSION_A2
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await screen.findByText('Ortak kabuk hazır')
    await waitFor(() => expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A2 }))

    // The held answer, authorized for the first session, arrives now.
    release!()
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(last(seen)).toMatchObject({ actor: USER_A, binding: SESSION_A2 })
    expect(seen.some((frame) => frame.binding === SESSION_A1 && frame.epoch >= 1)).toBe(false)
  })
})

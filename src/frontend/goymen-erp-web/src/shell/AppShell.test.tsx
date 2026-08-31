import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'

const IDENTITY = {
  userId: '11111111-1111-1111-1111-111111111111',
  displayName: 'Test Kullanıcı',
  email: 'test@example.com',
  allowedCompanyIds: ['aaaaaaaa-0000-0000-0000-000000000001', 'bbbbbbbb-0000-0000-0000-000000000002'],
}
const DIRECTORY = [
  { id: 'aaaaaaaa-0000-0000-0000-000000000001', code: 'GOY-A', legalName: 'Göymen A', isPlaceholder: true },
  { id: 'bbbbbbbb-0000-0000-0000-000000000002', code: 'GOY-B', legalName: 'Göymen B', isPlaceholder: true },
]
const CAP_A = { contractVersion: 'v1', generation: 'gen-a-1', capabilities: [] }
const CAP_B = { contractVersion: 'v1', generation: 'gen-b-1', capabilities: [] }

type Responder = (companyId: string | null) => Promise<Response> | Response

interface MockConfig {
  identity?: Responder
  directory?: Responder
  capabilities?: Responder
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function installFetch(config: MockConfig) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const companyId =
      (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
    if (url.includes('/identity/me')) return (config.identity ?? (() => json(IDENTITY)))(companyId)
    if (url.includes('/organization/companies'))
      return (config.directory ?? (() => json(DIRECTORY)))(companyId)
    if (url.includes('/shell/capabilities'))
      return (config.capabilities ??
        (() => json(companyId === DIRECTORY[1].id ? CAP_B : CAP_A)))(companyId)
    throw new Error(`unexpected fetch: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderShell() {
  const router = createMemoryRouter(shellRoutes, { initialEntries: ['/'] })
  render(
    <ShellProvider>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
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
    expect(screen.getByText('Test Kullanıcı')).toBeTruthy()
    // no mock module in the nav
    expect(screen.getByText(/henüz uygulanmış modül yok/i)).toBeTruthy()
  })

  it('shows "no authorized company" when the directory is empty', async () => {
    installFetch({ directory: () => json([]) })
    renderShell()
    expect(await screen.findByText(/Yetkili şirket yok/i)).toBeTruthy()
  })

  it('fails capability closed on 503 but still renders the base shell', async () => {
    installFetch({ capabilities: () => json({ title: 'x' }, 503) })
    renderShell()
    expect(await screen.findByText('Ortak kabuk hazır')).toBeTruthy()
    expect(screen.getByText(/yalnız temel kabuk açık/i)).toBeTruthy()
    expect(screen.getByText(/Yetenek bilgisi çözülene kadar modül gösterilmez/i)).toBeTruthy()
  })

  it('fails closed on a malformed directory response', async () => {
    installFetch({ directory: () => json({ not: 'an array' }) })
    renderShell()
    expect(await screen.findByText(/Beklenmeyen yanıt/i)).toBeTruthy()
  })
})

describe('SHELL-PILOT-01 company switch fence', () => {
  it('a slow old-company capability response after switching does not change the committed company', async () => {
    let releaseA: () => void = () => {}
    const gateA = new Promise<void>((resolve) => {
      releaseA = resolve
    })
    let capCallCount = 0
    installFetch({
      capabilities: async (companyId) => {
        capCallCount += 1
        if (companyId === DIRECTORY[0].id && capCallCount > 1) {
          // second time company A is asked (a stale re-request) — stall it
          await gateA
          return json(CAP_A)
        }
        return json(companyId === DIRECTORY[1].id ? CAP_B : CAP_A)
      },
    })
    renderShell()
    await screen.findByText('Ortak kabuk hazır')

    const select = screen.getByRole('combobox') as HTMLSelectElement
    // switch A -> B
    await userEvent.selectOptions(select, DIRECTORY[1].id)
    await waitFor(() => expect(select.value).toBe(DIRECTORY[1].id))

    // switch B -> A, but A's capability call is stalled
    await userEvent.selectOptions(select, DIRECTORY[0].id)
    // release the stalled A response now
    releaseA()
    await waitFor(() => expect(select.value).toBe(DIRECTORY[0].id))

    // committed company is A (the last attempt), and it is stable
    expect(screen.getByText('Göymen A')).toBeTruthy()
  })
})

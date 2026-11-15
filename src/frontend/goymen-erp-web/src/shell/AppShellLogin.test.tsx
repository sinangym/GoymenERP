/**
 * ID-1F — the shell in local-account mode: a 401 opens the sign-in screen instead of the terminal "Oturum sonlandı"
 * state, a 204 sign-in reads identity afresh and bootstraps, and sign-out ends the session and returns to sign-in.
 * All data SENTETIK.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { LOCAL_ACCOUNT_AUTH_MODE } from './authMode'
import { LOGIN_TEXT, LOGOUT_TEXT } from '../identity/identityText'

const COMPANY_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const USER_ID = '11111111-1111-1111-1111-111111111111'
const BINDING_1 = `v1.${'c1'.repeat(32)}`
const BINDING_2 = `v1.${'c2'.repeat(32)}`

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function identity(binding: string) {
  return {
    userId: USER_ID,
    displayName: 'Sentetik Kullanıcı',
    email: 'sentetik@example.invalid',
    allowedCompanyIds: [COMPANY_ID],
    authenticationSessionBinding: binding,
  }
}

interface Server {
  /** The binding of the server session the cookie names; null = no session (401 everywhere). */
  session: string | null
  capabilityStatus: number
  logins: number
  logouts: number
}

function installServer(server: Server) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    if (url.includes('/identity/session/login')) {
      server.logins += 1
      server.session = server.logins === 1 ? BINDING_1 : BINDING_2
      return new Response(null, { status: 204 })
    }
    if (url.includes('/identity/session/logout')) {
      server.logouts += 1
      server.session = null
      return new Response(null, { status: 204 })
    }
    if (server.session === null) return json({ title: 'unauthenticated' }, 401)
    if (url.includes('/identity/me')) return json(identity(server.session))
    if (url.includes('/organization/companies')) {
      return json([{ id: COMPANY_ID, code: 'SNT', legalName: 'Sentetik A.Ş.', isPlaceholder: true }])
    }
    if (url.includes('/shell/capabilities')) {
      if (server.capabilityStatus !== 200) return json({ title: 'refused' }, server.capabilityStatus)
      return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'gen-1', capabilities: [] })
    }
    throw new Error(`unexpected fetch: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderShell() {
  const router = createMemoryRouter(shellRoutes, { initialEntries: ['/'] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return queryClient
}

async function signIn() {
  const actor = userEvent.setup()
  await actor.type(await screen.findByLabelText(LOGIN_TEXT.userNameLabel), 'sentetik.kullanici')
  await actor.type(screen.getByLabelText(LOGIN_TEXT.passwordLabel), 'Sentetik-Parola-1')
  await actor.click(screen.getByRole('button', { name: LOGIN_TEXT.submit }))
}

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
  vi.stubEnv('VITE_AUTH_MODE', LOCAL_ACCOUNT_AUTH_MODE)
})
afterEach(() => {
  cleanup()
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AppShell in local-account mode — ID-1F', () => {
  it('a first visit without a session shows the sign-in screen, not the terminal state', async () => {
    installServer({ session: null, capabilityStatus: 200, logins: 0, logouts: 0 })
    renderShell()
    expect(await screen.findByRole('heading', { level: 1, name: LOGIN_TEXT.heading })).toBeTruthy()
    expect(screen.queryByText(/Oturum sonlandı/i)).toBeNull()
    expect(screen.queryByRole('status')).toBeNull()
    expect(document.querySelectorAll('main')).toHaveLength(1)
  })

  it('a 204 sign-in reads identity afresh and bootstraps the shell', async () => {
    const server: Server = { session: null, capabilityStatus: 200, logins: 0, logouts: 0 }
    installServer(server)
    renderShell()
    await signIn()
    expect(await screen.findByText('Ortak kabuk hazır')).toBeTruthy()
    expect(server.logins).toBe(1)
    expect(screen.getByRole('button', { name: LOGOUT_TEXT.action })).toBeTruthy()
  })

  it('never sends the development identity header', async () => {
    const fetchMock = installServer({ session: BINDING_1, capabilityStatus: 200, logins: 0, logouts: 0 })
    renderShell()
    await screen.findByText('Ortak kabuk hazır')
    for (const call of fetchMock.mock.calls) {
      const headers = (call[1]?.headers ?? {}) as Record<string, string>
      expect(Object.keys(headers).map((key) => key.toLowerCase())).not.toContain('x-development-user')
    }
  })

  it('sign-out ends the session, shows the signed-out notice and stops talking to the server', async () => {
    const server: Server = { session: BINDING_1, capabilityStatus: 200, logins: 0, logouts: 0 }
    const fetchMock = installServer(server)
    renderShell()
    await screen.findByText('Ortak kabuk hazır')
    await userEvent.setup().click(screen.getByRole('button', { name: LOGOUT_TEXT.action }))
    const status = await screen.findByRole('status')
    expect(status.getAttribute('data-notice')).toBe('signed-out')
    expect(server.logouts).toBe(1)
    const callsAfterLogout = fetchMock.mock.calls.length
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(fetchMock.mock.calls.length).toBe(callsAfterLogout)
  })

  it('a 401 on a session this tab held shows the expired notice; signing in again recovers', async () => {
    const server: Server = { session: BINDING_1, capabilityStatus: 401, logins: 1, logouts: 0 }
    installServer(server)
    renderShell()
    const status = await screen.findByRole('status')
    expect(status.getAttribute('data-notice')).toBe('expired')
    expect(status.textContent).toContain(LOGIN_TEXT.expiredTitle)

    server.capabilityStatus = 200
    await signIn()
    expect(await screen.findByText('Ortak kabuk hazır')).toBeTruthy()
    expect(server.logins).toBe(2)
  })

  it('a refused sign-in keeps the sign-in screen and commits nothing', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString()
        if (url.includes('/identity/session/login')) return json(null, 401)
        return json({ title: 'unauthenticated' }, 401)
      }),
    )
    renderShell()
    await signIn()
    const alert = await screen.findByRole('alert')
    expect(alert.getAttribute('data-problem')).toBe('refused')
    await waitFor(() => expect(screen.queryByText('Ortak kabuk hazır')).toBeNull())
  })
})

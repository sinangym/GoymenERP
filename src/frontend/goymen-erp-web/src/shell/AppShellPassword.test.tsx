import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { PASSWORD_TEXT as T } from '../identity/identityText'
import { LOCAL_ACCOUNT_AUTH_MODE } from './authMode'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const identity = { userId: '11111111-1111-1111-1111-111111111111', displayName: 'SENTETIK', email: null,
  allowedCompanyIds: [COMPANY], authenticationSessionBinding: null, mustChangePassword: true, localCredentialVersion: '3' }
const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

afterEach(() => { cleanup(); vi.unstubAllEnvs(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('Shell forced password fence', () => {
  it('reads no directory capabilities or business screen until the password change is confirmed', async () => {
    vi.stubGlobal('navigator', { onLine: true })
    vi.stubEnv('VITE_AUTH_MODE', LOCAL_ACCOUNT_AUTH_MODE)
    let forced = true
    const fetch = vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input)
      if (path.endsWith('/identity/me')) return json({ ...identity, mustChangePassword: forced, localCredentialVersion: forced ? '3' : '4' })
      if (path.endsWith('/identity/session/password')) { forced = false; return new Response(null, { status: 204 }) }
      if (path.endsWith('/organization/companies')) return json([{ id: COMPANY, code: 'SNT', legalName: 'SENTETIK', isPlaceholder: true }])
      if (path.includes('/shell/capabilities')) return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'SENTETIK', capabilities: [] })
      throw new Error('Unexpected test request')
    })
    vi.stubGlobal('fetch', fetch)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
    render(<ShellProvider queryClient={client}><RouterProvider router={createMemoryRouter(shellRoutes, { initialEntries: ['/'] })} /></ShellProvider>)
    expect(await screen.findByRole('heading', { name: T.heading })).toBeTruthy()
    expect(fetch.mock.calls.map(([url]) => String(url))).toEqual(['/api/v1/identity/me'])
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.current), 'SENTETIK-old')
    await user.type(screen.getByLabelText(T.next), 'SENTETIK-new')
    await user.type(screen.getByLabelText(T.confirm), 'SENTETIK-new')
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByText('Ortak kabuk hazır')).toBeTruthy()
    await waitFor(() => expect(fetch.mock.calls.some(([url]) => String(url).includes('/shell/capabilities'))).toBe(true))
    client.clear()
  })
})

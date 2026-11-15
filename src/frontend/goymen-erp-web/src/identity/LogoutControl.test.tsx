import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LogoutControl } from './LogoutControl'
import { LOGOUT_TEXT } from './identityText'
import { LOGOUT_PATH } from './loginApi'

function installFetch(answer: () => Response | Promise<Response>) {
  const fetchMock = vi.fn(async () => answer())
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function status(code: number): Response {
  return new Response(code === 204 ? null : '{}', { status: code })
}

async function clickSignOut() {
  render(<LogoutControl onSignedOut={onSignedOut} />)
  await userEvent.setup().click(screen.getByRole('button', { name: LOGOUT_TEXT.action }))
}

let onSignedOut = vi.fn()

beforeEach(() => {
  onSignedOut = vi.fn()
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('LogoutControl — ID-1F sign-out', () => {
  it('posts to the logout endpoint with the cookie and no body or development header', async () => {
    const fetchMock = installFetch(() => status(204))
    await clickSignOut()
    await waitFor(() => expect(onSignedOut).toHaveBeenCalledWith('signed-out'))
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url.endsWith(LOGOUT_PATH)).toBe(true)
    expect(init.method).toBe('POST')
    expect(init.credentials).toBe('same-origin')
    expect(init.body).toBeUndefined()
    const headers = init.headers as Record<string, string>
    expect(Object.keys(headers).map((key) => key.toLowerCase())).not.toContain('x-development-user')
  })

  it('a 401 means the session had already ended (KIM-D30) and is handed on as such', async () => {
    installFetch(() => status(401))
    await clickSignOut()
    await waitFor(() => expect(onSignedOut).toHaveBeenCalledWith('already-ended'))
  })

  it.each([
    [400, 'request-rejected', LOGOUT_TEXT.failed],
    [403, 'request-rejected', LOGOUT_TEXT.failed],
    [404, 'not-enabled', LOGOUT_TEXT.notEnabled],
    [500, 'service-unavailable', LOGOUT_TEXT.failed],
    [200, 'unexpected', LOGOUT_TEXT.unexpected],
  ])('a %i never pretends the sign-out worked', async (code, kind, text) => {
    installFetch(() => status(code))
    await clickSignOut()
    const alert = await screen.findByRole('alert')
    expect(alert.getAttribute('data-logout-failure')).toBe(kind)
    expect(alert.textContent).toBe(text)
    expect(onSignedOut).not.toHaveBeenCalled()
    // The control is usable again for a retry.
    expect((screen.getByRole('button', { name: LOGOUT_TEXT.action }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('tells offline from an unreachable server', async () => {
    installFetch(() => Promise.reject(new TypeError('fetch failed')))
    vi.stubGlobal('navigator', { onLine: false })
    await clickSignOut()
    expect((await screen.findByRole('alert')).getAttribute('data-logout-failure')).toBe('offline')
    cleanup()

    vi.stubGlobal('navigator', { onLine: true })
    await clickSignOut()
    expect((await screen.findByRole('alert')).getAttribute('data-logout-failure')).toBe('network-error')
    expect(onSignedOut).not.toHaveBeenCalled()
  })

  it('is disabled while the request is in flight', async () => {
    let release: (response: Response) => void = () => undefined
    installFetch(() => new Promise<Response>((resolve) => (release = resolve)))
    await clickSignOut()
    expect((screen.getByRole('button', { name: LOGOUT_TEXT.pending }) as HTMLButtonElement).disabled).toBe(true)
    release(status(204))
    await waitFor(() => expect(onSignedOut).toHaveBeenCalledWith('signed-out'))
  })
})

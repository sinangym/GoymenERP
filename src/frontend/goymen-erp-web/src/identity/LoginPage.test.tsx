import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LoginPage, type LoginNotice } from './LoginPage'
import { LOGIN_TEXT } from './identityText'
import { LOGIN_PATH } from './loginApi'
import { LOGIN_RATE_LIMITED_CODE } from './loginContract'

/** SENTETIK credentials — never a real account. */
const USER = 'sentetik.kullanici'
const PASSWORD = 'Sentetik-Parola-1'

function json(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

function noContent(): Response {
  return new Response(null, { status: 204 })
}

function rateLimitedBody() {
  return {
    type: 'https://example.invalid/problems/login-rate-limited',
    title: 'Too many sign-in attempts',
    status: 429,
    detail: 'Try again later.',
    code: LOGIN_RATE_LIMITED_CODE,
    correlationId: null,
  }
}

function installFetch(answer: () => Response | Promise<Response>) {
  const fetchMock = vi.fn(async () => answer())
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderLogin(notice: LoginNotice = null) {
  const onSignedIn = vi.fn()
  render(<LoginPage notice={notice} onSignedIn={onSignedIn} />)
  return onSignedIn
}

async function signIn(user = USER, password = PASSWORD) {
  const actor = userEvent.setup()
  if (user !== '') await actor.type(screen.getByLabelText(LOGIN_TEXT.userNameLabel), user)
  if (password !== '') await actor.type(screen.getByLabelText(LOGIN_TEXT.passwordLabel), password)
  await actor.click(screen.getByRole('button', { name: LOGIN_TEXT.submit }))
  return actor
}

beforeEach(() => {
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('LoginPage — ID-1F local sign-in', () => {
  it('labels both fields, uses the password-manager autocomplete hints and focuses the user name', () => {
    renderLogin()
    const userName = screen.getByLabelText(LOGIN_TEXT.userNameLabel)
    const password = screen.getByLabelText(LOGIN_TEXT.passwordLabel)
    expect(userName.getAttribute('autocomplete')).toBe('username')
    expect(password.getAttribute('autocomplete')).toBe('current-password')
    expect(password.getAttribute('type')).toBe('password')
    expect(document.activeElement).toBe(userName)
    expect(screen.getByRole('heading', { level: 1, name: LOGIN_TEXT.heading })).toBeTruthy()
  })

  it('posts the typed credentials as JSON with the cookie, and no development identity header', async () => {
    const fetchMock = installFetch(noContent)
    const onSignedIn = renderLogin()
    await signIn()
    await waitFor(() => expect(onSignedIn).toHaveBeenCalledTimes(1))
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url.endsWith(LOGIN_PATH)).toBe(true)
    expect(init.method).toBe('POST')
    expect(init.credentials).toBe('same-origin')
    expect(JSON.parse(String(init.body))).toEqual({ userName: USER, password: PASSWORD })
    const headers = init.headers as Record<string, string>
    expect(headers['Content-Type']).toBe('application/json')
    expect(Object.keys(headers).map((key) => key.toLowerCase())).not.toContain('x-development-user')
  })

  it('shows ONE uniform refusal on 401, with the neutral lock note, focused and announced', async () => {
    installFetch(() => json({ code: 'account_locked', title: 'Locked' }, 401))
    const onSignedIn = renderLogin()
    await signIn()
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain(LOGIN_TEXT.refused)
    expect(alert.textContent).toContain(LOGIN_TEXT.lockNote)
    // The server's body is never shown: nothing tells a locked account from a wrong password.
    expect(alert.textContent).not.toContain('Locked')
    expect(alert.getAttribute('data-problem')).toBe('refused')
    expect(alert.parentElement?.getAttribute('aria-live')).toBe('assertive')
    await waitFor(() => expect(document.activeElement).toBe(alert))
    expect(onSignedIn).not.toHaveBeenCalled()
  })

  it('clears the password after an answer and keeps the user name', async () => {
    installFetch(() => json(null, 401))
    renderLogin()
    await signIn()
    await screen.findByRole('alert')
    expect((screen.getByLabelText(LOGIN_TEXT.userNameLabel) as HTMLInputElement).value).toBe(USER)
    expect((screen.getByLabelText(LOGIN_TEXT.passwordLabel) as HTMLInputElement).value).toBe('')
  })

  it('announces a repeated refusal again as a new notice', async () => {
    installFetch(() => json(null, 401))
    renderLogin()
    await signIn()
    const first = await screen.findByRole('alert')
    await signIn('', PASSWORD)
    await waitFor(() => expect(screen.getByRole('alert')).not.toBe(first))
    expect(screen.getByRole('alert').textContent).toContain(LOGIN_TEXT.refused)
  })

  it.each([
    ['rate-limited', () => json(rateLimitedBody(), 429), LOGIN_TEXT.rateLimitedTitle],
    ['unexpected', () => json({ title: 'Too many' }, 429), LOGIN_TEXT.unexpectedTitle],
    ['not-enabled', () => json(null, 404), LOGIN_TEXT.notEnabledTitle],
    ['request-rejected', () => json(null, 400), LOGIN_TEXT.requestRejectedTitle],
    ['request-rejected', () => json(null, 415), LOGIN_TEXT.requestRejectedTitle],
    ['service-unavailable', () => json(null, 503), LOGIN_TEXT.serviceUnavailableTitle],
    ['unexpected', () => json(null, 200), LOGIN_TEXT.unexpectedTitle],
  ])('shows the %s state', async (kind, answer, title) => {
    installFetch(answer)
    const onSignedIn = renderLogin()
    await signIn()
    const alert = await screen.findByRole('alert')
    expect(alert.getAttribute('data-problem')).toBe(kind)
    expect(alert.textContent).toContain(title)
    expect(alert.textContent).not.toContain(LOGIN_TEXT.refused)
    expect(onSignedIn).not.toHaveBeenCalled()
  })

  it('tells offline from an unreachable server', async () => {
    installFetch(() => Promise.reject(new TypeError('fetch failed')))
    vi.stubGlobal('navigator', { onLine: false })
    renderLogin()
    await signIn()
    expect((await screen.findByRole('alert')).getAttribute('data-problem')).toBe('offline')
    cleanup()

    vi.stubGlobal('navigator', { onLine: true })
    renderLogin()
    await signIn()
    expect((await screen.findByRole('alert')).getAttribute('data-problem')).toBe('network-error')
  })

  it('does not send a request while a field is empty', async () => {
    const fetchMock = installFetch(noContent)
    renderLogin()
    await signIn(USER, '')
    const alert = await screen.findByRole('alert')
    expect(alert.getAttribute('data-problem')).toBe('missing-fields')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('disables the submit button while the request is in flight', async () => {
    let release: (response: Response) => void = () => undefined
    installFetch(() => new Promise<Response>((resolve) => (release = resolve)))
    const onSignedIn = renderLogin()
    await signIn()
    const pending = screen.getByRole('button', { name: LOGIN_TEXT.submitting }) as HTMLButtonElement
    expect(pending.disabled).toBe(true)
    release(noContent())
    await waitFor(() => expect(onSignedIn).toHaveBeenCalledTimes(1))
  })

  it('shows and hides the password with a keyboard-reachable toggle', async () => {
    renderLogin()
    const actor = userEvent.setup()
    const password = screen.getByLabelText(LOGIN_TEXT.passwordLabel)
    const toggle = screen.getByRole('button', { name: LOGIN_TEXT.showPassword })
    expect(toggle.getAttribute('type')).toBe('button')
    expect(toggle.getAttribute('aria-controls')).toBe(password.id)
    await actor.click(toggle)
    expect(password.getAttribute('type')).toBe('text')
    await actor.click(screen.getByRole('button', { name: LOGIN_TEXT.hidePassword }))
    expect(password.getAttribute('type')).toBe('password')
  })

  it.each([
    ['expired', LOGIN_TEXT.expiredTitle],
    ['signed-out', LOGIN_TEXT.signedOutTitle],
  ] as const)('shows the %s notice as a status', (notice, title) => {
    renderLogin(notice)
    const status = screen.getByRole('status')
    expect(status.getAttribute('data-notice')).toBe(notice)
    expect(status.textContent).toContain(title)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('shows no notice on a first visit', () => {
    renderLogin(null)
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
  })
})

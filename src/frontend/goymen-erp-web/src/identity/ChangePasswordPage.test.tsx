import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChangePasswordPage } from './ChangePasswordPage'
import { PASSWORD_TEXT as T } from './identityText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

function mount() {
  const changed = vi.fn()
  const signedOut = vi.fn()
  render(<ChangePasswordPage expectedVersion="7" onChanged={changed} onSignedOut={signedOut} />)
  return { changed, signedOut }
}

async function fill() {
  const user = userEvent.setup()
  await user.type(screen.getByLabelText(T.current), 'SENTETIK-old')
  await user.type(screen.getByLabelText(T.next), 'SENTETIK-new')
  await user.type(screen.getByLabelText(T.confirm), 'SENTETIK-new')
  await user.click(screen.getByRole('button', { name: T.submit }))
  return user
}

describe('ID-2F required password change', () => {
  it('sends the credential version and untrimmed passwords only in the POST body', async () => {
    vi.stubGlobal('navigator', { onLine: true })
    const fetch = vi.fn(async () => new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetch)
    const { changed } = mount()
    await fill()
    await waitFor(() => expect(changed).toHaveBeenCalledOnce())
    const [path, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(path).toMatch(/\/identity\/session\/password$/)
    expect(init.credentials).toBe('same-origin')
    expect(JSON.parse(String(init.body))).toEqual({ expectedVersion: '7', currentPassword: 'SENTETIK-old', newPassword: 'SENTETIK-new' })
    expect((screen.getByLabelText(T.current) as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText(T.next) as HTMLInputElement).value).toBe('')
  })

  it('retains one frozen attempt and the same key after an unknown outcome', async () => {
    vi.stubGlobal('navigator', { onLine: true })
    const fetch = vi.fn(async () => new Response(null, { status: 503 }))
    vi.stubGlobal('fetch', fetch)
    mount()
    const user = await fill()
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T['service-unavailable'])
    expect((screen.getByLabelText(T.next) as HTMLInputElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    const requests = fetch.mock.calls as unknown as [string, RequestInit][]
    expect(requests[1][1].body).toBe(requests[0][1].body)
    expect(new Headers(requests[1][1].headers).get('Idempotency-Key')).toBe(new Headers(requests[0][1].headers).get('Idempotency-Key'))
  })

  it('clears secrets after a confirmed refusal and focuses its notice', async () => {
    vi.stubGlobal('navigator', { onLine: true })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ type: 'about:blank', title: 'SENTETIK', status: 400, detail: 'SENTETIK', code: 'current_password_invalid' }), { status: 400 })))
    mount()
    await fill()
    const notice = await screen.findByRole('alert')
    expect(notice.textContent).toBe(T.currentInvalid)
    expect(document.activeElement).toBe(notice)
    expect((screen.getByLabelText(T.current) as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText(T.next) as HTMLInputElement).disabled).toBe(false)
  })

  it.each([401, 403, 404, 409])('handles a %i response without claiming success', async (status) => {
    vi.stubGlobal('navigator', { onLine: true })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(status === 409 ? JSON.stringify({ type: 'about:blank', title: 'SENTETIK', detail: 'SENTETIK', status, code: 'version_conflict' }) : null, { status })))
    const callbacks = mount()
    await fill()
    if (status === 401) await waitFor(() => expect(callbacks.signedOut).toHaveBeenCalledWith('already-ended'))
    else expect(await screen.findByRole('alert')).toBeTruthy()
    expect(callbacks.changed).not.toHaveBeenCalled()
  })
})

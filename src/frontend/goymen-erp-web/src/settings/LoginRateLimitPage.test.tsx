/**
 * KIM-D34 component cases — Ayarlar > Giriş deneme sınırı.
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures; the real API, SQL and login path are the
 * backend's Settings login rate limit tests. What these cases establish: the states (loading, empty with the configured
 * fallback named, unavailable with retry, off-contract answer, ungranted route), that the decision's starting value is a
 * reminder and never pre-fills the form, what is SENT (company header, Idempotency-Key, the exact body with the expected
 * version), invalid input held back, the same key on a retry after an unknown outcome, the replay notice, the stale refusal
 * that closes the form and re-reads, and a definite refusal kept inside the form.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, IDENTITY, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { LOGIN_RATE_LIMIT_TEXT as T, partitionLabel, windowLabel } from './loginRateLimitText'

const PATH = '/ayarlar/giris-deneme-siniri'
const PROFILE = 'cccccccc-0000-4000-8000-00000000000c'

function reset() {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
}

afterEach(reset)

const isRead = (url: string) => url.endsWith('/settings/login-rate-limit')
const isRecord = (url: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && url.endsWith('/settings/login-rate-limit/versions')

function entry(
  version: string,
  permitLimit: number,
  windowSeconds: number,
  partitionBy: string,
  isCurrent: boolean,
  trustedProxies: readonly string[] = [],
) {
  return {
    profileId: PROFILE,
    version,
    permitLimit,
    windowSeconds,
    partitionBy,
    trustedProxies,
    isCurrent,
    recordedBy: IDENTITY.userId,
    recordedInCompanyId: COMPANY_A,
    recordedAtUtc: '2026-11-15T09:00:00+00:00',
    reason: `SENTETIK sürüm ${version}`,
  }
}

function recordedView() {
  return {
    version: '2',
    versions: [
      entry('2', 7, 120, 'IpAddressAndUserName', true, ['192.0.2.10', '198.51.100.0/24']),
      entry('1', 25, 60, 'IpAddress', false),
    ],
  }
}

const emptyView = () => ({ version: '0', versions: [] })

function install(respond: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.loginRateLimit], respond })
}

async function openForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: T.newVersion }))
}

describe('Giriş deneme sınırı — reads', () => {
  it('shows the loading state while the read is open', async () => {
    install((url) => (isRead(url) ? new Promise<Response>(() => undefined) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull())
  })

  it('shows the stored versions newest first and the current one as the server marked it', async () => {
    const recorded = install((url) => (isRead(url) ? json(recordedView()) : undefined))
    renderAt(PATH)

    const table = await screen.findByRole('table', { name: T.tableLabel })
    const rows = within(table).getAllByRole('row')
    expect(rows).toHaveLength(3)
    expect(within(rows[1]).getByText(T.isCurrent)).toBeTruthy()
    expect(within(rows[1]).getByText(windowLabel(120))).toBeTruthy()
    expect(within(rows[1]).getByText(partitionLabel('IpAddressAndUserName'))).toBeTruthy()
    expect(within(rows[2]).getByText(T.isPrevious)).toBeTruthy()
    expect(within(rows[2]).getByText(partitionLabel('IpAddress'))).toBeTruthy()
    expect(within(rows[1]).getByText('192.0.2.10, 198.51.100.0/24')).toBeTruthy()
    expect(within(rows[2]).getByText(T.trustedProxiesNone)).toBeTruthy()

    const read = recorded.find((request) => isRead(request.url))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('shows the empty state naming the configured fallback and states no number of its own', async () => {
    install((url) => (isRead(url) ? json(emptyView()) : undefined))
    renderAt(PATH)

    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.getByText(T.noneCurrent)).toBeTruthy()
    expect(screen.queryByRole('table', { name: T.tableLabel })).toBeNull()
  })

  it('offers a retry when the source is unavailable, and reads again on it', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url) => {
      if (!isRead(url)) return undefined
      reads += 1
      return reads === 1 ? problem(503, { code: 'settings_provider_unavailable' }) : json(recordedView())
    })
    renderAt(PATH)

    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('table', { name: T.tableLabel })).toBeTruthy()
  })

  it('shows nothing of an off-contract answer (two current entries, an unknown partition, an extra key, a bad proxy list)', async () => {
    const view = recordedView()
    const offContract = [
      { ...view, versions: [view.versions[0], { ...view.versions[1], isCurrent: true }] },
      { ...view, versions: [{ ...view.versions[0], partitionBy: 'UserName' }, view.versions[1]] },
      { ...view, permitLimit: 10 },
      { ...view, versions: [{ ...view.versions[0], trustedProxies: ['proxy.example'] }, view.versions[1]] },
      { ...view, versions: [{ ...view.versions[0], trustedProxies: ['192.0.2.10', '192.0.2.10'] }, view.versions[1]] },
      { ...view, versions: [view.versions[0], { ...view.versions[1], trustedProxies: undefined }] },
    ]
    for (const body of offContract) {
      install((url) => (isRead(url) ? json(body) : undefined))
      renderAt(PATH)
      await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
      expect(screen.queryByRole('table', { name: T.tableLabel })).toBeNull()
      reset()
    }
  })

  it('an ungranted screen calls nothing', async () => {
    const recorded = installFetch({ capabilities: [], respond: () => json(recordedView()) })
    renderAt(PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
  })
})

describe('Giriş deneme sınırı — commands', () => {
  it('starts the first version empty, shows the decision value as a hint only, and sends what was typed', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(emptyView())
      }
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    expect(screen.getByText(T.decisionHint)).toBeTruthy()
    const permit = screen.getByLabelText(T.permitLabel, { exact: false }) as HTMLInputElement
    const windowInput = screen.getByLabelText(T.windowLabel, { exact: false }) as HTMLInputElement
    expect(permit.value).toBe('')
    expect(windowInput.value).toBe('')
    for (const radio of screen.getAllByRole('radio')) expect((radio as HTMLInputElement).checked).toBe(false)

    await user.type(permit, ' 12 ')
    await user.type(windowInput, '90')
    await user.click(screen.getByRole('radio', { name: partitionLabel('IpAddressAndUserName') }))
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), '  SENTETIK gerekçe  ')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.method).toBe('POST')
    expect(command.url).toMatch(/\/settings\/login-rate-limit\/versions$/)
    expect(command.body).toEqual({
      expectedVersion: '0',
      permitLimit: 12,
      windowSeconds: 90,
      partitionBy: 'IpAddressAndUserName',
      trustedProxies: [],
      reason: 'SENTETIK gerekçe',
    })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })

  it('pre-fills from the current stored version and sends its version as the expected one', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    expect((screen.getByLabelText(T.permitLabel, { exact: false }) as HTMLInputElement).value).toBe('7')
    expect((screen.getByLabelText(T.windowLabel, { exact: false }) as HTMLInputElement).value).toBe('120')
    expect(
      (screen.getByRole('radio', { name: partitionLabel('IpAddressAndUserName') }) as HTMLInputElement).checked,
    ).toBe(true)
    expect((screen.getByLabelText(T.trustedProxiesLabel, { exact: false }) as HTMLTextAreaElement).value).toBe(
      '192.0.2.10\n198.51.100.0/24',
    )
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '2',
      permitLimit: 7,
      windowSeconds: 120,
      partitionBy: 'IpAddressAndUserName',
      trustedProxies: ['192.0.2.10', '198.51.100.0/24'],
      reason: 'SENTETIK',
    })
  })

  it('sends the typed trusted proxy list one entry per line, and an emptied list as an empty one', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    const proxies = screen.getByLabelText(T.trustedProxiesLabel, { exact: false })
    await user.clear(proxies)
    await user.type(proxies, ' 2001:db8::/64 {Enter}{Enter}203.0.113.5')
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect((commands(recorded)[0].body as { trustedProxies: unknown }).trustedProxies).toEqual([
      '2001:db8::/64',
      '203.0.113.5',
    ])
  })

  it('holds back a malformed or repeated trusted proxy entry', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    const proxies = screen.getByLabelText(T.trustedProxiesLabel, { exact: false })
    await user.clear(proxies)
    await user.type(proxies, 'proxy.example')
    expect(screen.getByText(T.trustedProxiesError)).toBeTruthy()
    await user.clear(proxies)
    await user.type(proxies, '192.0.2.10{Enter}192.0.2.10')
    expect(screen.getByText(T.trustedProxiesError)).toBeTruthy()
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    fireEvent.submit(screen.getByRole('button', { name: T.save }).closest('form') as HTMLFormElement)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(commands(recorded)).toEqual([])
  })

  it('holds back a count or window the server would not read', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    const permit = screen.getByLabelText(T.permitLabel, { exact: false })
    await user.clear(permit)
    await user.type(permit, '0')
    expect(screen.getByText(T.permitError)).toBeTruthy()
    const windowInput = screen.getByLabelText(T.windowLabel, { exact: false })
    await user.clear(windowInput)
    await user.type(windowInput, '1.5')
    expect(screen.getByText(T.windowError)).toBeTruthy()
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    fireEvent.submit(screen.getByRole('button', { name: T.save }).closest('form') as HTMLFormElement)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(commands(recorded)).toEqual([])
  })

  it('keeps the same Idempotency-Key for the retry after an unknown outcome and shows the replay notice', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      if (!isRecord(url, init)) return undefined
      attempts += 1
      return attempts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
    })
    renderAt(PATH)

    await openForm(user)
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.retrySame }))

    expect(await screen.findByText(T.savedReplay)).toBeTruthy()
    const [first, second] = commands(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('closes the form and reads again on a version conflict', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(recordedView())
      }
      return isRecord(url, init) ? problem(409, { code: 'settings_login_rate_limit_version_conflict' }) : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.staleReloaded)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('shows a definite refusal inside the form and keeps it open', async () => {
    const user = userEvent.setup()
    install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? problem(403, { code: 'settings_access_denied' }) : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect((await screen.findByRole('alert')).textContent).toBe('Bu işlem için yetkiniz yok.')
    expect(screen.getByRole('button', { name: T.save })).toBeTruthy()
  })
})

/**
 * UI-K02 "Denetim kaydı" — read states with the shell and the transport mocked, so each outcome is driven exactly:
 * the auth boundary report on 401/403, offline vs network error, 503 retry, the 400 split (filter vs no filter),
 * a forbidden continuation, and a late answer after the view was left. SENTETIK data; DOM evidence only.
 */

import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuditLogPage } from './AuditLogPage'
import { decodeAuditEventPage, type AuditEventPage } from './contract'
import type { ViewOutcome } from '../shell/uiState'
import { COMPANY_A, auditEvent, page } from './testHarness'

const api = vi.hoisted(() => ({ fetchPage: vi.fn() }))
const scenario = vi.hoisted(() => ({
  companyId: 'aaaaaaaa-0000-0000-0000-000000000001' as string,
  scopeMode: 'single' as 'single' | 'group',
  namespace: 'SENTETIK-a' as string | null,
  granted: true,
  report: vi.fn(),
}))
vi.mock('./api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./api')>()),
  fetchAuditEventPage: api.fetchPage,
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1,
    routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: scenario.companyId, scopeMode: scenario.scopeMode, capabilities: [] },
  }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => (scenario.namespace === null ? null : [scenario.namespace]),
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) => capability === 'identity.audit.list' && scenario.granted,
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ namespace: scenario.namespace }), report: scenario.report }),
}))

function ok(items: readonly Record<string, unknown>[], nextCursor: string | null = null): ViewOutcome<AuditEventPage> {
  return { type: 'ok-single', data: decodeAuditEventPage(page(items, nextCursor), COMPANY_A)! }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}

function mount(path = '/ayarlar/denetim-kaydi') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuditLogPage />
    </MemoryRouter>,
  )
}

const stateOf = async (text: RegExp) => (await screen.findByText(text)).closest('[data-state]')?.getAttribute('data-state')

beforeEach(() => {
  api.fetchPage.mockReset()
  scenario.report.mockReset()
  scenario.namespace = 'SENTETIK-a'
  scenario.scopeMode = 'single'
  scenario.granted = true
})

afterEach(() => cleanup())

describe('Denetim kaydı — read states', () => {
  it('shows the loading state until the first page answers', async () => {
    const answer = deferred<ViewOutcome<AuditEventPage>>()
    api.fetchPage.mockReturnValueOnce(answer.promise)
    mount()
    expect(await stateOf(/Yükleniyor/)).toBe('initial-loading')
    answer.resolve(ok([auditEvent()]))
    expect(await screen.findByRole('table', { name: 'Denetim olayları' })).toBeTruthy()
  })

  it.each([
    [401, /Oturum sonlandı/, 'unauthenticated'],
    [403, /Yetkiniz yok/, 'forbidden'],
  ])('reports a first-page %s to the auth boundary and shows its own state', async (status, text, kind) => {
    api.fetchPage.mockResolvedValueOnce({ type: 'http', status, body: null })
    mount()
    expect(await stateOf(text)).toBe(kind)
    expect(scenario.report).toHaveBeenCalledWith(status, { namespace: 'SENTETIK-a' })
    expect(screen.queryByRole('table')).toBeNull()
  })

  it.each([
    [{ type: 'offline' }, /Bağlantı yok/, 'offline'],
    [{ type: 'network-error' }, /Sunucuya ulaşılamadı/, 'network-error'],
    [{ type: 'http', status: 503, body: null }, /Servis geçici olarak kullanılamıyor/, 'service-unavailable'],
    [{ type: 'http', status: 500, body: null }, /Servis geçici olarak kullanılamıyor/, 'service-unavailable'],
    [{ type: 'malformed' }, /Beklenmeyen yanıt/, 'malformed-response'],
  ] as const)('shows %o as its own state and retries the first page', async (outcome, text, kind) => {
    const user = userEvent.setup()
    api.fetchPage.mockResolvedValueOnce(outcome).mockResolvedValueOnce(ok([auditEvent()]))
    mount()
    expect(await stateOf(text)).toBe(kind)
    expect(scenario.report).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('table', { name: 'Denetim olayları' })).toBeTruthy()
    expect(api.fetchPage).toHaveBeenLastCalledWith(COMPANY_A, expect.any(Object), null, expect.any(AbortSignal))
  })

  it('reads a 400 as an invalid filter only when a filter was sent; otherwise it fails closed', async () => {
    api.fetchPage.mockResolvedValueOnce({ type: 'http', status: 400, body: null })
    mount('/ayarlar/denetim-kaydi?kayitNo=SENTETIK')
    expect(await stateOf(/Filtre geçersiz/)).toBe('invalid-filter')
    expect(api.fetchPage).toHaveBeenCalledWith(
      COMPANY_A,
      { correlationId: '', entityType: '', entityId: 'SENTETIK' },
      null,
      expect.any(AbortSignal),
    )
    cleanup()

    api.fetchPage.mockResolvedValueOnce({ type: 'http', status: 400, body: null })
    mount()
    expect(await stateOf(/Beklenmeyen yanıt/)).toBe('malformed-response')
  })

  it('drops every shown row when a continuation is forbidden and reports it', async () => {
    const user = userEvent.setup()
    api.fetchPage.mockResolvedValueOnce(ok([auditEvent()], 'SENTETIK-c')).mockResolvedValueOnce({ type: 'http', status: 403, body: null })
    mount()
    await screen.findByRole('table', { name: 'Denetim olayları' })
    await user.click(screen.getByRole('button', { name: 'Daha fazla' }))
    expect(await stateOf(/Yetkiniz yok/)).toBe('forbidden')
    expect(screen.queryByRole('table')).toBeNull()
    expect(scenario.report).toHaveBeenCalledWith(403, { namespace: 'SENTETIK-a' })
  })

  it('keeps the rows on a malformed next page and offers a retry of the same cursor', async () => {
    const user = userEvent.setup()
    api.fetchPage
      .mockResolvedValueOnce(ok([auditEvent()], 'SENTETIK-c'))
      .mockResolvedValueOnce({ type: 'malformed' })
      .mockResolvedValueOnce(ok([auditEvent()]))
    mount()
    await screen.findByRole('table', { name: 'Denetim olayları' })
    await user.click(screen.getByRole('button', { name: 'Daha fazla' }))
    expect(await screen.findByText(/Sonraki sayfanın yanıtı beklenen biçimde değil/)).toBeTruthy()
    expect(screen.getByText('1 olay gösteriliyor.')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByText('2 olay gösteriliyor.')).toBeTruthy()
    expect(api.fetchPage.mock.calls.map((call) => call[2])).toEqual([null, 'SENTETIK-c', 'SENTETIK-c'])
  })

  it('applies no late answer after the view is left', async () => {
    const answer = deferred<ViewOutcome<AuditEventPage>>()
    api.fetchPage.mockReturnValueOnce(answer.promise)
    const view = mount()
    await waitFor(() => expect(api.fetchPage).toHaveBeenCalledTimes(1))
    const signal = api.fetchPage.mock.calls[0][3] as AbortSignal
    view.unmount()
    expect(signal.aborted).toBe(true)
    answer.resolve({ type: 'http', status: 401, body: null })
    await Promise.resolve()
    expect(scenario.report).not.toHaveBeenCalled()
  })

  it('reads nothing in a group context or without the capability', async () => {
    scenario.scopeMode = 'group'
    mount()
    expect(await stateOf(/Yetkili şirket yok/)).toBe('no-authorized-company')
    cleanup()
    scenario.scopeMode = 'single'
    scenario.granted = false
    mount()
    expect(await stateOf(/Bulunamadı/)).toBe('not-found')
    expect(api.fetchPage).not.toHaveBeenCalled()
  })
})

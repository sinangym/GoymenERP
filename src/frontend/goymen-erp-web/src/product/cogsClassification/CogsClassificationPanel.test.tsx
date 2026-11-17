/** Synthetic component sources. These do not claim endpoint, SQL, grant or browser acceptance. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { contextKey, contextKeyString, initialShellState, type ShellState } from '../../shell/shellState'
import type { ViewOutcome } from '../../shell/uiState'
import { PRODUCT_CAPABILITY, PRODUCT_SET_COGS_CLASSIFICATION_CAPABILITY } from '../capabilities'
import { fetchCogs, setCogs, type CogsCommandOutcome } from './api'
import { cogsAttempt, cogsBinding, resetCogsAttemptsForTests } from './attempt'
import { COGS_SOURCE, type CogsView } from './contract'
import { CogsClassificationPanel } from './CogsClassificationPanel'
import { COGS_TEXT as T } from './text'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const OTHER = '33333333-3333-3333-3333-333333333333'
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ actorId: 'synthetic-actor', sessionEpoch: 1,
  companyId: '11111111-1111-1111-1111-111111111111', committedContextRevision: 1,
  requestFenceGeneration: 1, capabilityGeneration: 'g1', namespace: 'synthetic-namespace' })), report: vi.fn() }))
let currentShell: ShellState
vi.mock('../../shell/shellContext', () => ({ useShellState: () => currentShell }))
vi.mock('../../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
vi.mock('./api', () => ({ COGS_API_BASE: '/api/v1', fetchCogs: vi.fn(), setCogs: vi.fn() }))
const clients: QueryClient[] = []
const initial = (overrides: Partial<CogsView> = {}): CogsView => ({ companyId: COMPANY, productId: PRODUCT, productVersion: '7',
  classificationVersion: '0', classification: null, changedInCompanyId: null, updatedAtUtc: null, sourceVersion: COGS_SOURCE, ...overrides })
const assigned = (overrides: Partial<CogsView> = {}): CogsView => initial({ classificationVersion: '1', classification: 1,
  changedInCompanyId: COMPANY, updatedAtUtc: '2026-11-17T10:00:00Z', ...overrides })
function mount(overrides: { companyId?: string; productId?: string; namespace?: string } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0, refetchOnWindowFocus: false } } })
  clients.push(client)
  const element = () => <QueryClientProvider client={client}><CogsClassificationPanel
    companyId={overrides.companyId ?? currentShell.activeContext?.companyId ?? COMPANY} productId={overrides.productId ?? PRODUCT}
    queryNamespace={overrides.namespace ?? (contextKey(currentShell) === null ? '' : contextKeyString(contextKey(currentShell)!))} /></QueryClientProvider>
  const result = render(element())
  return { ...result, client, refresh: () => result.rerender(element()) }
}
async function edit(kind: '1' | '2' = '1') {
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: T.edit }))
  await user.selectOptions(screen.getByLabelText(T.classification), kind)
  await user.type(screen.getByLabelText(T.reason), 'SENTETIK gerekçe')
  return user
}
beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(fetchCogs).mockReset(); vi.mocked(setCogs).mockReset()
  let key = 0
  vi.stubGlobal('crypto', { randomUUID: () => `synthetic-key-${++key}` })
  currentShell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'synthetic-actor', scopeMode: 'single', companyId: COMPANY, capabilityGeneration: 'g1',
      capabilities: [PRODUCT_CAPABILITY.productsDetail, PRODUCT_SET_COGS_CLASSIFICATION_CAPABILITY] } }
  vi.mocked(fetchCogs).mockResolvedValue({ type: 'ok-single', data: initial() })
  vi.mocked(setCogs).mockResolvedValue({ type: 'applied', data: { classification: assigned(), isReplay: false, originalCorrelationId: 'synthetic-original' } })
})
afterEach(() => {
  cleanup(); clients.splice(0).forEach((client) => client.clear()); resetCogsAttemptsForTests(); vi.unstubAllGlobals()
})

describe('COGS read and authority boundaries', () => {
  it('shows loading until the actual source resolves and offers no premature edit', () => {
    vi.mocked(fetchCogs).mockReturnValue(new Promise(() => {}))
    mount()
    expect(screen.getByText(/Yükleniyor/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
  it('keeps initial null visibly unset without choosing an account or kind', async () => {
    mount()
    expect(await screen.findByText(T.unset)).toBeTruthy()
    expect(screen.getByText(T.shared)).toBeTruthy()
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: T.edit }))
    expect((screen.getByLabelText(T.classification) as HTMLSelectElement).value).toBe('')
    expect(document.body.textContent).not.toMatch(/621|620/)
  })
  it('allows global read without deriving COGS write from another existing Product grant', async () => {
    currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, capabilities: [
      PRODUCT_CAPABILITY.productsDetail, PRODUCT_CAPABILITY.productsEdit, PRODUCT_CAPABILITY.productsCreate,
    ] } }
    mount()
    expect(await screen.findByText(T.unset)).toBeTruthy()
    expect(screen.getByText(T.readOnly)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
    expect(setCogs).not.toHaveBeenCalled()
  })
  it('does not let the new write child capability imply global Product read', () => {
    currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, capabilities: [PRODUCT_SET_COGS_CLASSIFICATION_CAPABILITY] } }
    mount()
    expect(screen.getByText(/Yetkiniz yok/)).toBeTruthy()
    expect(fetchCogs).not.toHaveBeenCalled()
  })
  it('explains that the actual global class was last changed in another company', async () => {
    vi.mocked(fetchCogs).mockResolvedValue({ type: 'ok-single', data: assigned({ changedInCompanyId: OTHER, classification: 2 }) })
    mount()
    expect(await screen.findByText(T.changedElsewhere)).toBeTruthy()
    expect(screen.getByText(T.manufactured)).toBeTruthy()
  })
  it.each<readonly [ViewOutcome<CogsView>, string]>([
    [{ type: 'http', status: 401 }, 'Oturum sonlandı'], [{ type: 'http', status: 403 }, 'Yetkiniz yok'],
    [{ type: 'http', status: 404 }, 'Bulunamadı'], [{ type: 'http', status: 409 }, 'Çakışma'],
    [{ type: 'http', status: 503 }, 'Servis geçici olarak kullanılamıyor'], [{ type: 'offline' }, 'Bağlantı yok'],
    [{ type: 'network-error' }, 'Sunucuya ulaşılamadı'], [{ type: 'malformed' }, 'Beklenmeyen yanıt'],
  ])('renders a distinct safe owner read result %j', async (outcome, text) => {
    vi.mocked(fetchCogs).mockResolvedValue(outcome)
    mount()
    expect(await screen.findByText(new RegExp(text))).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
    if (outcome.type === 'http') expect(security.report).toHaveBeenCalledWith(outcome.status, security.capture.mock.results[0].value)
  })
  it.each([{ companyId: OTHER }, { productId: 'invalid' }, { namespace: 'foreign-namespace' }])('refuses a stale route binding before fetch %j', (props) => {
    mount(props)
    expect(fetchCogs).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
  it('does not read without a committed single-company context', () => {
    currentShell = { ...currentShell, activeContext: null }
    mount()
    expect(fetchCogs).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
})

describe('COGS explicit mutation and retained outcome', () => {
  it('refreshes only the current product history after an admitted successful write', async () => {
    const mounted = mount()
    const namespace = contextKeyString(contextKey(currentShell)!)
    const ownHistory = [namespace, currentShell.routeEntryGeneration, 'product-history', PRODUCT]
    const foreignHistory = ['foreign-namespace', currentShell.routeEntryGeneration, 'product-history', PRODUCT]
    mounted.client.setQueryDefaults(ownHistory, { gcTime: Infinity })
    mounted.client.setQueryDefaults(foreignHistory, { gcTime: Infinity })
    mounted.client.setQueryData(ownHistory, { synthetic: 'own' })
    mounted.client.setQueryData(foreignHistory, { synthetic: 'foreign' })
    const user = await edit()
    await user.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(mounted.client.getQueryState(ownHistory)?.isInvalidated).toBe(true))
    expect(mounted.client.getQueryState(foreignHistory)?.isInvalidated).toBe(false)
  })
  it('sends only the explicit numeric kind, trimmed reason and actual Product/class revisions', async () => {
    vi.mocked(fetchCogs).mockResolvedValueOnce({ type: 'ok-single', data: assigned({ classificationVersion: '9007199254740993' }) })
      .mockResolvedValue({ type: 'ok-single', data: assigned({ classification: 2, classificationVersion: '9007199254740994' }) })
    vi.mocked(setCogs).mockResolvedValue({ type: 'applied', data: { classification: assigned({ classification: 2, classificationVersion: '9007199254740994' }),
      isReplay: false, originalCorrelationId: 'synthetic-original' } })
    mount()
    const user = await edit('2')
    await user.clear(screen.getByLabelText(T.reason)); await user.type(screen.getByLabelText(T.reason), ' SENTETIK açık seçim ')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(setCogs).toHaveBeenCalledWith(COMPANY, PRODUCT, { expectedProductVersion: '7', expectedClassificationVersion: '9007199254740993',
      classification: 2, reason: 'SENTETIK açık seçim' }, 'cogs-synthetic-key-1')
    expect(await screen.findByText(T.saved)).toBeTruthy()
    await waitFor(() => expect(fetchCogs).toHaveBeenCalledTimes(2))
  })
  it('never preselects even an already assigned class and refuses missing explicit choice or reason', async () => {
    vi.mocked(fetchCogs).mockResolvedValue({ type: 'ok-single', data: assigned() })
    mount(); const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.edit }))
    expect((screen.getByLabelText(T.classification) as HTMLSelectElement).value).toBe('')
    expect((screen.getByLabelText(T.reason) as HTMLInputElement).maxLength).toBe(1000)
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(screen.getByText(T.required)).toBeTruthy()
    expect(setCogs).not.toHaveBeenCalled()
  })
  it.each(['\u0001', '\u007f', 'a'.repeat(1001)])('refuses a non-canonical reason without changing a value %j', async (reason) => {
    mount(); await edit()
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: reason } })
    fireEvent.submit(screen.getByRole('form', { name: T.form }))
    expect(screen.getByText(T.required)).toBeTruthy()
    expect(setCogs).not.toHaveBeenCalled()
  })
  it('freezes an ambiguous command and retries the exact same key and body', async () => {
    vi.mocked(setCogs).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' }).mockResolvedValueOnce({ type: 'applied',
      data: { classification: assigned(), isReplay: true, originalCorrelationId: 'synthetic-original' } })
    mount(); const user = await edit()
    await user.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.reason).closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(setCogs).toHaveBeenCalledTimes(2))
    expect(vi.mocked(setCogs).mock.calls[1]).toEqual(vi.mocked(setCogs).mock.calls[0])
    expect(await screen.findByText(T.replayed)).toBeTruthy()
  })
  it('retains the unknown request through a route unmount and return', async () => {
    vi.mocked(setCogs).mockResolvedValueOnce({ type: 'unknown', reason: 'offline' }).mockResolvedValueOnce({ type: 'applied',
      data: { classification: assigned(), isReplay: true, originalCorrelationId: 'synthetic-original' } })
    const first = mount(); const user = await edit()
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    const original = vi.mocked(setCogs).mock.calls[0]
    first.unmount(); mount()
    await screen.findByText(T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(setCogs).toHaveBeenCalledTimes(2))
    expect(vi.mocked(setCogs).mock.calls[1]).toEqual(original)
  })
  it.each([401, 403])('reports write auth refusal %i without forgetting a preceding unknown', async (status) => {
    vi.mocked(setCogs).mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' })
      .mockResolvedValueOnce({ type: 'refused', status, terminalReplay: false })
    mount(); const user = await edit()
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    expect(await screen.findByText(T.unknownRefusal)).toBeTruthy()
    expect(security.report).toHaveBeenCalledWith(status, expect.anything())
    expect((screen.getByLabelText(T.reason).closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true)
  })
  it.each([404, 409])('refreshes the owner after a definite stored terminal refusal %i', async (status) => {
    vi.mocked(setCogs).mockResolvedValue({ type: 'refused', status, terminalReplay: true })
    mount(); const user = await edit()
    await user.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(fetchCogs).toHaveBeenCalledTimes(2))
    expect(await screen.findByRole('button', { name: T.edit })).toBeTruthy()
    expect(screen.queryByRole('form', { name: T.form })).toBeNull()
  })
  it('does not show another company pending key or result after a committed context switch', async () => {
    let settle!: (result: CogsCommandOutcome) => void
    vi.mocked(setCogs).mockReturnValueOnce(new Promise((resolve) => { settle = resolve }))
    const mounted = mount(); const user = await edit()
    await user.click(screen.getByRole('button', { name: T.save }))
    currentShell = { ...currentShell, committedContextRevision: 2, requestFenceGeneration: 2,
      activeContext: { ...currentShell.activeContext!, companyId: OTHER, capabilityGeneration: 'g2' } }
    vi.mocked(fetchCogs).mockResolvedValue({ type: 'ok-single', data: initial({ companyId: OTHER }) })
    mounted.refresh(); await screen.findByText(T.unset)
    await act(async () => { settle({ type: 'applied', data: { classification: assigned(), isReplay: false, originalCorrelationId: 'synthetic-original' } }) })
    expect(screen.queryByText(T.saved)).toBeNull()
    expect(screen.queryByText(T.unknown)).toBeNull()
    expect(cogsAttempt(cogsBinding(currentShell, PRODUCT)).phase).toBe('idle')
  })
  it('keeps a late same-company reply under former authority unknown until an explicit fresh-authority replay', async () => {
    let settle!: (result: CogsCommandOutcome) => void
    vi.mocked(setCogs).mockReturnValueOnce(new Promise((resolve) => { settle = resolve }))
    const mounted = mount(); const user = await edit()
    await user.click(screen.getByRole('button', { name: T.save }))
    const original = vi.mocked(setCogs).mock.calls[0]
    currentShell = { ...currentShell, requestFenceGeneration: currentShell.requestFenceGeneration + 1 }
    mounted.refresh(); await screen.findByText(T.unknown)
    await act(async () => { settle({ type: 'applied', data: { classification: assigned(), isReplay: false, originalCorrelationId: 'synthetic-original' } }) })
    expect(screen.queryByText(T.saved)).toBeNull()
    expect(cogsAttempt(cogsBinding(currentShell, PRODUCT)).phase).toBe('unknown')
    vi.mocked(setCogs).mockResolvedValueOnce({ type: 'applied', data: { classification: assigned(), isReplay: true, originalCorrelationId: 'synthetic-original' } })
    await user.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(setCogs).toHaveBeenCalledTimes(2))
    expect(vi.mocked(setCogs).mock.calls[1]).toEqual(original)
  })
  it('keeps unresolved retry read-only after the separate write capability is revoked', async () => {
    vi.mocked(setCogs).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
    const mounted = mount(); const user = await edit()
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, capabilities: [PRODUCT_CAPABILITY.productsDetail] } }
    mounted.refresh()
    expect(await screen.findByText(T.restoreAuthority)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
  it('discards a former actor read without releasing its data to the replacement actor', async () => {
    let settle!: (result: ViewOutcome<CogsView>) => void
    vi.mocked(fetchCogs).mockReturnValueOnce(new Promise((resolve) => { settle = resolve }))
    const mounted = mount()
    currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, userId: 'synthetic-replacement' } }
    vi.mocked(fetchCogs).mockResolvedValue({ type: 'ok-single', data: initial() })
    mounted.refresh(); await screen.findByText(T.unset)
    await act(async () => { settle({ type: 'ok-single', data: assigned({ classification: 2 }) }) })
    expect(screen.queryByText(T.manufactured)).toBeNull()
    expect(screen.getByText(T.unset)).toBeTruthy()
  })
})

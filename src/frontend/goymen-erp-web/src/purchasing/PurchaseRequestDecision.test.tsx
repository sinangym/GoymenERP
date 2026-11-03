import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseRequestDetailPage } from './PurchaseRequestDetailPage'
import { fetchPurchaseRequest } from './api'
import { purchaseRequestDetailPath } from './capabilities'
import type { PurchaseRequestDetail, PurchaseRequestStatus } from './contract'
import { decidePurchaseRequest, type RequestDecisionOutcome } from './requestDecisionApi'

const scenario = vi.hoisted(() => ({ canDecide: true, report: vi.fn() }))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const REQUEST = '66666666-6666-4666-8666-666666666666'
const AT = '2026-11-02T10:00:00Z'
const clients: QueryClient[] = []

vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: ACTOR, companyId: COMPANY, scopeMode: 'single', capabilities: [] },
  }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => ['SENTETIK-context-a'],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) =>
    capability === 'purchasing.requests.approve' ? scenario.canDecide : capability === 'purchasing.requests.detail',
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: scenario.report }),
}))
vi.mock('./api', () => ({ fetchPurchaseRequest: vi.fn() }))
vi.mock('./requestSubmissionApi', () => ({ submitPurchaseRequest: vi.fn() }))
vi.mock('./requestDecisionApi', () => ({ decidePurchaseRequest: vi.fn() }))

function detail(status: PurchaseRequestStatus, version: string): PurchaseRequestDetail {
  return {
    request: { id: REQUEST, companyId: COMPANY, status, version, effectiveOn: '2026-11-02', createdAtUtc: AT, updatedAtUtc: AT, lineCount: 0 },
    lines: [],
    approval: status === 'Approved' || status === 'Rejected' ? { decidedBy: 'SENTETIK-manager', decidedAtUtc: AT, reason: null } : null,
    rejection: status === 'Rejected' ? { decidedBy: 'SENTETIK-manager', decidedAtUtc: AT, reason: 'SENTETIK ihtiyaç kalktı' } : null,
  }
}

function applied(status: 'Approved' | 'Rejected', version: string, replayed = false): RequestDecisionOutcome {
  return { type: 'applied', replayed, response: { detail: detail(status, version), isReplay: replayed, originalCorrelationId: 'corr-1' } }
}

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[{ pathname: purchaseRequestDetailPath(REQUEST), state: null }]}>
      <Routes><Route path="/satinalma/talepler/:requestId" element={<PurchaseRequestDetailPage />} /></Routes>
    </MemoryRouter>
  </QueryClientProvider>)
}

async function ready() { await screen.findByText(REQUEST, { exact: true }) }

beforeEach(() => {
  scenario.canDecide = true; scenario.report.mockReset()
  vi.mocked(fetchPurchaseRequest).mockReset().mockResolvedValue({ type: 'ok-single', data: detail('Submitted', '3') })
  vi.mocked(decidePurchaseRequest).mockReset()
  sessionStorage.clear()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('P-01-DETAIL request decision (A1-1)', () => {
  it('offers no decision without the purchasing.requests.approve child capability', async () => {
    scenario.canDecide = false; mount(); await ready()
    expect(screen.queryByRole('button', { name: 'Talebi onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Talebi reddet' })).toBeNull()
  })

  it('approves a submitted request with its version and a fresh key, then re-reads it', async () => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue(applied('Approved', '4'))
    const user = userEvent.setup(); mount(); await ready()
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Approved', '4') })
    await user.click(screen.getByRole('button', { name: 'Talebi onayla' }))
    await screen.findByText('Talep onaylandı ve yeniden okundu.')
    const [company, request, kind, version, key, reason] = vi.mocked(decidePurchaseRequest).mock.calls[0]
    expect([company, request, kind, version, reason]).toEqual([COMPANY, REQUEST, 'approval', '3', undefined])
    expect(key).toMatch(/^[0-9a-f-]{36}$/)
    await waitFor(() => expect(fetchPurchaseRequest).toHaveBeenCalledTimes(2))
    await screen.findByText('Onaylandı')
    // An approved request can still be rejected (No.161 §1), but not approved again.
    expect(screen.queryByRole('button', { name: 'Talebi onayla' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Talebi reddet' })).toBeTruthy()
  })

  it('requires a reason before a rejection is sent and sends it as written', async () => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue(applied('Rejected', '4'))
    const user = userEvent.setup(); mount(); await ready()
    await user.click(screen.getByRole('button', { name: 'Talebi reddet' }))
    expect(screen.getByRole('alert').textContent).toContain('Ret gerekçesi boş olamaz.')
    await user.type(screen.getByLabelText('Ret gerekçesi'), '   ')
    await user.click(screen.getByRole('button', { name: 'Talebi reddet' }))
    expect(decidePurchaseRequest).not.toHaveBeenCalled()
    await user.clear(screen.getByLabelText('Ret gerekçesi'))
    await user.type(screen.getByLabelText('Ret gerekçesi'), 'SENTETIK ihtiyaç kalktı')
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Rejected', '4') })
    await user.click(screen.getByRole('button', { name: 'Talebi reddet' }))
    await screen.findByText('Talep reddedildi ve yeniden okundu.')
    expect(vi.mocked(decidePurchaseRequest).mock.calls[0].slice(2, 4)).toEqual(['rejection', '3'])
    expect(vi.mocked(decidePurchaseRequest).mock.calls[0][5]).toBe('SENTETIK ihtiyaç kalktı')
    await screen.findByText('Reddedildi')
    expect(screen.getByText('SENTETIK ihtiyaç kalktı')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Talebi reddet' })).toBeNull()
  })

  it('shows the busy state while deciding and allows no second click', async () => {
    let release!: (outcome: RequestDecisionOutcome) => void
    vi.mocked(decidePurchaseRequest).mockImplementation(() => new Promise((resolve) => { release = resolve }))
    const user = userEvent.setup(); mount(); await ready()
    await user.click(screen.getByRole('button', { name: 'Talebi onayla' }))
    const busy = screen.getByRole('button', { name: 'Onaylanıyor…' }) as HTMLButtonElement
    expect(busy.disabled).toBe(true)
    expect(screen.queryByRole('button', { name: 'Talebi reddet' })).toBeNull()
    release(applied('Approved', '4'))
    await screen.findByText('Talep onaylandı ve yeniden okundu.')
    expect(decidePurchaseRequest).toHaveBeenCalledTimes(1)
  })

  it('keeps an unknown result and resends exactly the same key, version and reason', async () => {
    vi.mocked(decidePurchaseRequest).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const user = userEvent.setup(); mount(); await ready()
    await user.type(screen.getByLabelText('Ret gerekçesi'), 'SENTETIK neden')
    await user.click(screen.getByRole('button', { name: 'Talebi reddet' }))
    await screen.findByText(/Sonuç bilinmiyor/)
    expect(screen.queryByRole('button', { name: 'Talebi onayla' })).toBeNull()
    vi.mocked(decidePurchaseRequest).mockResolvedValueOnce(applied('Rejected', '4', true))
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Rejected', '4') })
    await user.click(screen.getByRole('button', { name: 'Aynı reddi yeniden gönder' }))
    await screen.findByText('Önceki karar yanıtı yeniden gösterildi.')
    const [first, second] = vi.mocked(decidePurchaseRequest).mock.calls
    expect(second).toEqual(first)
    expect(sessionStorage.length).toBe(0)
  })

  it('recalls an unresolved decision after a reload and offers only the same resend', async () => {
    sessionStorage.setItem(`goymen.purchasing.request-decision.v1:1:${ACTOR}:${COMPANY}:${REQUEST}`, JSON.stringify({
      actorId: ACTOR, companyId: COMPANY, requestId: REQUEST, kind: 'approval', expectedVersion: '3',
      idempotencyKey: '99999999-9999-4999-8999-999999999999', reason: null,
    }))
    mount(); await ready()
    expect(screen.getByRole('button', { name: 'Aynı onayı yeniden gönder' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Talebi onayla' })).toBeNull()
  })

  it.each([
    ['purchasing_request_same_actor', 'Talebi hazırlayan veya gönderen aynı talep için karar veremez.'],
    ['purchasing_request_has_approved_order', 'Talebe bağlı onaylı sipariş var; önce bağlı sipariş iptal edilmelidir.'],
    ['version_conflict', 'Talep bu sırada değişti; güncel sürüm yeniden okundu.'],
  ])('explains the 409 %s and re-reads the record', async (code, text) => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue({ type: 'failed', status: 409, code, replayed: false })
    const user = userEvent.setup(); mount(); await ready()
    await user.click(screen.getByRole('button', { name: 'Talebi onayla' }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain(text)
    expect(alert.textContent).toContain('Kayıt yeniden okundu.')
    await waitFor(() => expect(fetchPurchaseRequest).toHaveBeenCalledTimes(2))
  })

  it.each([401, 403])('reports HTTP %s to the auth boundary', async (status) => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue({ type: 'failed', status, code: null, replayed: false })
    const user = userEvent.setup(); mount(); await ready()
    await user.click(screen.getByRole('button', { name: 'Talebi onayla' }))
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(status, { token: 1 }))
  })

  // #900 review P3: the rejection path's own 409 re-read, its 403 boundary and the 4000-character bound.
  it('refuses a reason longer than 4000 characters before anything is sent', async () => {
    const user = userEvent.setup(); mount(); await ready()
    await user.click(screen.getByLabelText('Ret gerekçesi'))
    await user.paste('x'.repeat(4001))
    await user.click(screen.getByRole('button', { name: 'Talebi reddet' }))
    expect(screen.getByRole('alert').textContent).toContain('Ret gerekçesi 4000 karakteri aşamaz; kısaltın.')
    expect(decidePurchaseRequest).not.toHaveBeenCalled()
    expect(sessionStorage.length).toBe(0)
    // Exactly 4000 characters is the bound itself and is sent as written.
    vi.mocked(decidePurchaseRequest).mockResolvedValue(applied('Rejected', '4'))
    await user.clear(screen.getByLabelText('Ret gerekçesi'))
    await user.paste('y'.repeat(4000))
    await user.click(screen.getByRole('button', { name: 'Talebi reddet' }))
    await screen.findByText('Talep reddedildi ve yeniden okundu.')
    expect(vi.mocked(decidePurchaseRequest).mock.calls[0][5]).toHaveLength(4000)
  })

  it('re-reads the record after a 409 on a rejection and keeps no unresolved snapshot', async () => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue({ type: 'failed', status: 409, code: 'purchasing_request_decision_state', replayed: false })
    const user = userEvent.setup(); mount(); await ready()
    await user.type(screen.getByLabelText('Ret gerekçesi'), 'SENTETIK neden')
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Rejected', '5') })
    await user.click(screen.getByRole('button', { name: 'Talebi reddet' }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Talep bu karar için uygun durumda değil.')
    expect(alert.textContent).toContain('Kayıt yeniden okundu.')
    await waitFor(() => expect(fetchPurchaseRequest).toHaveBeenCalledTimes(2))
    await screen.findByText('Reddedildi')
    expect(screen.queryByRole('button', { name: 'Talebi reddet' })).toBeNull()
    expect(sessionStorage.length).toBe(0)
  })

  it('reports a 403 on a rejection to the auth boundary without a re-read or a resend offer', async () => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue({ type: 'failed', status: 403, code: null, replayed: false })
    const user = userEvent.setup(); mount(); await ready()
    await user.type(screen.getByLabelText('Ret gerekçesi'), 'SENTETIK neden')
    await user.click(screen.getByRole('button', { name: 'Talebi reddet' }))
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(403, { token: 1 }))
    expect((await screen.findByRole('alert')).textContent).toContain('Karar kaydedilmedi (403)')
    expect(fetchPurchaseRequest).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('button', { name: 'Aynı reddi yeniden gönder' })).toBeNull()
    expect(sessionStorage.length).toBe(0)
  })

  it('offers no decision on a draft or a rejected request', async () => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Rejected', '5') })
    mount(); await ready()
    expect(screen.queryByRole('button', { name: /Talebi (onayla|reddet)/ })).toBeNull()
    cleanup()
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Draft', '2') })
    mount(); await ready()
    expect(screen.queryByRole('button', { name: /Talebi (onayla|reddet)/ })).toBeNull()
  })
})

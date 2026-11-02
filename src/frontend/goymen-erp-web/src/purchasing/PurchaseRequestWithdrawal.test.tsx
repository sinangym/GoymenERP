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

const scenario = vi.hoisted(() => ({ canWithdraw: true, report: vi.fn() }))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const OTHER = '44444444-4444-4444-8444-444444444444'
const REQUEST = '66666666-6666-4666-8666-666666666666'
const AT = '2026-11-02T10:00:00Z'
const REASON_LABEL = 'Geri çekme gerekçesi'
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
    capability === 'purchasing.requests.withdraw' ? scenario.canWithdraw : capability === 'purchasing.requests.detail',
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: scenario.report }),
}))
vi.mock('./api', () => ({ fetchPurchaseRequest: vi.fn() }))
vi.mock('./requestSubmissionApi', () => ({ submitPurchaseRequest: vi.fn() }))
vi.mock('./requestDecisionApi', () => ({ decidePurchaseRequest: vi.fn() }))

function detail(status: PurchaseRequestStatus, version: string, submittedBy: string | null = ACTOR, preparedBy: string | null = ACTOR): PurchaseRequestDetail {
  return {
    request: { id: REQUEST, companyId: COMPANY, status, version, effectiveOn: '2026-11-02', createdAtUtc: AT, updatedAtUtc: AT, lineCount: 0 },
    lines: [],
    approval: status === 'Approved' ? { decidedBy: 'SENTETIK-manager', decidedAtUtc: AT, reason: null } : null,
    rejection: null,
    withdrawal: status === 'Withdrawn' ? { decidedBy: submittedBy ?? ACTOR, decidedAtUtc: AT, reason: 'SENTETIK yanlış ürün' } : null,
    preparedBy,
    submittedBy: status === 'Draft' ? null : submittedBy,
  }
}

function applied(version: string, replayed = false): RequestDecisionOutcome {
  return { type: 'applied', replayed, response: { detail: detail('Withdrawn', version), isReplay: replayed, originalCorrelationId: 'corr-1' } }
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
  scenario.canWithdraw = true; scenario.report.mockReset()
  vi.mocked(fetchPurchaseRequest).mockReset().mockResolvedValue({ type: 'ok-single', data: detail('Submitted', '3') })
  vi.mocked(decidePurchaseRequest).mockReset()
  sessionStorage.clear()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('P-01-DETAIL request withdrawal (A1-1b, ALS-D46, ALS-D47)', () => {
  it('offers no withdrawal without the purchasing.requests.withdraw child capability', async () => {
    scenario.canWithdraw = false; mount(); await ready()
    expect(screen.queryByRole('button', { name: 'Geri çek' })).toBeNull()
  })

  it('offers no withdrawal to anyone but a recorded preparer or submitter', async () => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Submitted', '3', OTHER, OTHER) })
    mount(); await ready()
    expect(screen.queryByRole('button', { name: 'Geri çek' })).toBeNull()
    cleanup()
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Submitted', '3', null, null) })
    mount(); await ready()
    expect(screen.queryByRole('button', { name: 'Geri çek' })).toBeNull()
  })

  it.each([
    ['the preparer when someone else submitted', OTHER, ACTOR],
    ['the submitter when someone else prepared', ACTOR, OTHER],
    ['the preparer of a request with no recorded submitter', null, ACTOR],
  ])('offers the withdrawal to %s (ALS-D47)', async (_name, submittedBy, preparedBy) => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Submitted', '3', submittedBy, preparedBy) })
    mount(); await ready()
    expect(screen.getByRole('button', { name: 'Geri çek' })).toBeTruthy()
  })

  it.each(['Draft', 'Approved', 'Withdrawn'] as const)('offers no withdrawal on a %s request', async (status) => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail(status, '4') })
    mount(); await ready()
    expect(screen.queryByRole('button', { name: 'Geri çek' })).toBeNull()
  })

  it('requires a reason, refuses one over 4000 characters, then withdraws with the version and a fresh key and re-reads', async () => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue(applied('4'))
    const user = userEvent.setup(); mount(); await ready()
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    expect(screen.getByRole('alert').textContent).toContain('Geri çekme gerekçesi boş olamaz.')
    await user.type(screen.getByLabelText(REASON_LABEL), '   ')
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    await user.clear(screen.getByLabelText(REASON_LABEL))
    await user.click(screen.getByLabelText(REASON_LABEL))
    await user.paste('x'.repeat(4001))
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    expect(screen.getByRole('alert').textContent).toContain('Geri çekme gerekçesi 4000 karakteri aşamaz; kısaltın.')
    expect(decidePurchaseRequest).not.toHaveBeenCalled()
    await user.clear(screen.getByLabelText(REASON_LABEL))
    await user.type(screen.getByLabelText(REASON_LABEL), 'SENTETIK yanlış ürün')
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Withdrawn', '4') })
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    await screen.findByText('Talep geri çekildi ve yeniden okundu.')
    const [company, request, kind, version, key, reason] = vi.mocked(decidePurchaseRequest).mock.calls[0]
    expect([company, request, kind, version, reason]).toEqual([COMPANY, REQUEST, 'withdrawal', '3', 'SENTETIK yanlış ürün'])
    expect(key).toMatch(/^[0-9a-f-]{36}$/)
    await waitFor(() => expect(fetchPurchaseRequest).toHaveBeenCalledTimes(2))
    await screen.findByText('Geri çekildi')
    expect(screen.getByText('SENTETIK yanlış ürün')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Geri çek' })).toBeNull()
  })

  it('shows the busy state while withdrawing and allows no second click', async () => {
    let release!: (outcome: RequestDecisionOutcome) => void
    vi.mocked(decidePurchaseRequest).mockImplementation(() => new Promise((resolve) => { release = resolve }))
    const user = userEvent.setup(); mount(); await ready()
    await user.type(screen.getByLabelText(REASON_LABEL), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    const busy = screen.getByRole('button', { name: 'Geri çekiliyor…' }) as HTMLButtonElement
    expect(busy.disabled).toBe(true)
    release(applied('4'))
    await screen.findByText('Talep geri çekildi ve yeniden okundu.')
    expect(decidePurchaseRequest).toHaveBeenCalledTimes(1)
  })

  it('keeps an unknown result and resends exactly the same key, version and reason', async () => {
    vi.mocked(decidePurchaseRequest).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const user = userEvent.setup(); mount(); await ready()
    await user.type(screen.getByLabelText(REASON_LABEL), 'SENTETIK neden')
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    await screen.findByText(/Sonuç bilinmiyor/)
    expect(screen.queryByLabelText(REASON_LABEL)).toBeNull()
    vi.mocked(decidePurchaseRequest).mockResolvedValueOnce(applied('4', true))
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail('Withdrawn', '4') })
    await user.click(screen.getByRole('button', { name: 'Aynı geri çekmeyi yeniden gönder' }))
    await screen.findByText('Önceki geri çekme yanıtı yeniden gösterildi.')
    const [first, second] = vi.mocked(decidePurchaseRequest).mock.calls
    expect(second).toEqual(first)
    expect(sessionStorage.length).toBe(0)
  })

  it('recalls an unresolved withdrawal after a reload and offers only the same resend', async () => {
    sessionStorage.setItem(`goymen.purchasing.request-withdrawal.v1:1:${ACTOR}:${COMPANY}:${REQUEST}`, JSON.stringify({
      actorId: ACTOR, companyId: COMPANY, requestId: REQUEST, expectedVersion: '3',
      idempotencyKey: '99999999-9999-4999-8999-999999999999', reason: 'SENTETIK',
    }))
    mount(); await ready()
    expect(screen.getByRole('button', { name: 'Aynı geri çekmeyi yeniden gönder' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Geri çek' })).toBeNull()
  })

  it.each([
    ['purchasing_request_not_preparer', 'Talebi yalnız oluşturan veya gönderen kişi geri çekebilir.'],
    ['purchasing_request_decision_state', 'Yalnız gönderilmiş ve henüz karar verilmemiş talep geri çekilir.'],
    ['purchasing_request_has_approved_order', 'Talebe bağlı onaylı sipariş var; önce bağlı sipariş iptal edilmelidir.'],
    ['version_conflict', 'Talep bu sırada değişti; güncel sürüm yeniden okundu.'],
  ])('explains the 409 %s and re-reads the record', async (code, text) => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue({ type: 'failed', status: 409, code, replayed: false })
    const user = userEvent.setup(); mount(); await ready()
    await user.type(screen.getByLabelText(REASON_LABEL), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain(text)
    expect(alert.textContent).toContain('Kayıt yeniden okundu.')
    await waitFor(() => expect(fetchPurchaseRequest).toHaveBeenCalledTimes(2))
    expect(sessionStorage.length).toBe(0)
  })

  it.each([401, 403])('reports HTTP %s to the auth boundary without a re-read', async (status) => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue({ type: 'failed', status, code: null, replayed: false })
    const user = userEvent.setup(); mount(); await ready()
    await user.type(screen.getByLabelText(REASON_LABEL), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(status, { token: 1 }))
    expect(fetchPurchaseRequest).toHaveBeenCalledTimes(1)
    expect(sessionStorage.length).toBe(0)
  })

  it('shows a 422 reason refusal from the server in user words', async () => {
    vi.mocked(decidePurchaseRequest).mockResolvedValue({ type: 'failed', status: 422, code: 'purchasing_request_withdrawal_reason_required', replayed: false })
    const user = userEvent.setup(); mount(); await ready()
    await user.type(screen.getByLabelText(REASON_LABEL), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: 'Geri çek' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Geri çekme gerekçesi zorunludur.')
    expect(fetchPurchaseRequest).toHaveBeenCalledTimes(1)
  })
})

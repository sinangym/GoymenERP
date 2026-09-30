import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import detailRaw from './examples/detail-response.json?raw'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { SalesDraftRemainderChangePanel } from './SalesDraftRemainderChangePanel'
import { resetPendingRemainderActions } from './pendingRemainderChanges'

const authBoundary = vi.hoisted(() => ({ capture: vi.fn(() => ({})), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => authBoundary }))

function approvedBody(version = '4', quantity = '5'): Record<string, any> {
  const body = JSON.parse(detailRaw) as Record<string, any>
  body.draft.approvalState = 'Approved'
  body.draft.version = version
  body.approval = {
    submittedBy: 'sales-preparer',
    submittedAtUtc: '2026-09-19T09:00:00Z',
    decidedBy: 'sales-approver',
    decidedAtUtc: '2026-09-19T09:05:00Z',
    rejectionReason: null,
  }
  body.lines[0].quantity = quantity
  body.lines[0].requestedDeliverySlices = null
  return body
}

function detail(version = '4', quantity = '5'): DraftDetail {
  const body = approvedBody(version, quantity)
  const decoded = decodeDraftDetail(body, body.draft.id as string)
  if (decoded === null) throw new Error('fixture must decode')
  return decoded
}

const remainderRead = (lineId: string, unitId: string) => ({
  draftVersion: '4', floorStatus: 'Readable',
  lines: [{ lineId, lineNo: 1, orderUnitId: unitId, quantity: '5', remainder: 'Open', loaded: '2', shipped: '2', delivered: '1', minimumQuantity: '2' }],
  items: [],
})

const remainderChange = (lineId: string, state: 'Pending' | 'Approved') => ({
  changeNo: 1, revision: 'Rev.0', lineId, lineNo: 1, kind: 'Revise', state,
  orderedQuantity: '5', requestedQuantity: '3', reason: 'SENTETIK müşteri kalanı değiştirdi',
  requestedBy: 'sales-preparer', requestedAtUtc: '2026-09-30T10:00:00Z',
  loadedAtRequest: '2', shippedAtRequest: '2', deliveredAtRequest: '1',
  decidedBy: state === 'Approved' ? 'sales-approver' : null,
  decidedAtUtc: state === 'Approved' ? '2026-09-30T10:05:00Z' : null,
  decisionReason: null, resultQuantity: state === 'Approved' ? '3' : null,
  loadedAtDecision: state === 'Approved' ? '2' : null,
})

afterEach(() => { cleanup(); resetPendingRemainderActions(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('S-04 partial-delivery remainder panel', () => {
  it('shows owner quantities and sends a revise request under one idempotency key', async () => {
    const current = detail()
    const calls: { method: string; key: string | null; body: unknown }[] = []
    vi.stubGlobal('fetch', vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase()
      if (method === 'GET') return new Response(JSON.stringify(remainderRead(current.lines[0].lineId, current.lines[0].orderUnitId)), { status: 200 })
      const headers = init?.headers as Record<string, string>
      calls.push({ method, key: headers['Idempotency-Key'] ?? null, body: JSON.parse(String(init?.body)) })
      return new Response(JSON.stringify({ detail: approvedBody('5'), isReplay: false, originalCorrelationId: 'c-1' }), {
        status: 200, headers: { 'Content-Type': 'application/json' },
      })
    }))
    const recorded = vi.fn()
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><SalesDraftRemainderChangePanel detail={current} companyId={current.draft.companyId}
      namespace="test-company" actorId="actor-a" routeEntryGeneration={1}
      attemptBinding='["actor-a","session-1","company-a","draft-a"]'
      canRequest canDecide={false} onRecorded={recorded} /></QueryClientProvider>)

    const panel = await screen.findByTestId('sales-remainder-changes')
    expect(await within(panel).findByText(/Yüklenen: 2/)).toBeTruthy()
    fireEvent.change(within(panel).getByLabelText('İşlem'), { target: { value: 'Revise' } })
    fireEvent.change(within(panel).getByLabelText('Yeni sipariş miktarı'), { target: { value: '3' } })
    fireEvent.change(within(panel).getByLabelText('Gerekçe'), { target: { value: 'SENTETIK müşteri kalan miktarı düşürdü' } })
    fireEvent.click(within(panel).getByRole('button', { name: 'Onaya gönder' }))

    await waitFor(() => expect(recorded).toHaveBeenCalledTimes(1))
    expect(calls).toHaveLength(1)
    expect(calls[0].key).toMatch(/.+/)
    expect(calls[0].body).toEqual({ expectedVersion: '4', lineId: current.lines[0].lineId, kind: 'Revise', quantity: '3', reason: 'SENTETIK müşteri kalan miktarı düşürdü' })
  })

  it('approves the pending change and reopens the same owner facts at the new version', async () => {
    const current = detail()
    let decided = false
    const posts: { url: string; body: unknown }[] = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase()
      if (method === 'GET') {
        const read = remainderRead(current.lines[0].lineId, current.lines[0].orderUnitId)
        return new Response(JSON.stringify({
          ...read,
          draftVersion: decided ? '5' : '4',
          lines: [{ ...read.lines[0], quantity: decided ? '3' : '5', remainder: decided ? 'Revised' : 'Open' }],
          items: [remainderChange(current.lines[0].lineId, decided ? 'Approved' : 'Pending')],
        }), { status: 200 })
      }

      posts.push({ url: String(input), body: JSON.parse(String(init?.body)) })
      decided = true
      return new Response(JSON.stringify({ detail: approvedBody('5', '3'), isReplay: false, originalCorrelationId: 'c-2' }), {
        status: 200, headers: { 'Content-Type': 'application/json' },
      })
    }))

    const recorded = vi.fn()
    const firstClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={firstClient}><SalesDraftRemainderChangePanel detail={current} companyId={current.draft.companyId}
      namespace="test-company" actorId="approver-a" routeEntryGeneration={1}
      attemptBinding='["approver-a","session-1","company-a","draft-a"]'
      canRequest={false} canDecide onRecorded={recorded} /></QueryClientProvider>)

    const pending = await screen.findByTestId('sales-remainder-change-1')
    fireEvent.click(within(pending).getByRole('button', { name: 'Onayla' }))
    await waitFor(() => expect(recorded).toHaveBeenCalledTimes(1))
    expect(posts).toHaveLength(1)
    expect(posts[0].url).toMatch(/\/remainder-changes\/1\/approval$/)
    expect(posts[0].body).toEqual({ expectedVersion: '4', reason: null })

    cleanup()
    const reopened = detail('5', '3')
    const secondClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={secondClient}><SalesDraftRemainderChangePanel detail={reopened} companyId={reopened.draft.companyId}
      namespace="test-company" actorId="approver-a" routeEntryGeneration={2}
      attemptBinding='["approver-a","session-1","company-a","draft-a"]'
      canRequest={false} canDecide onRecorded={vi.fn()} /></QueryClientProvider>)

    const reopenedPanel = await screen.findByTestId('sales-remainder-changes')
    expect(await within(reopenedPanel).findByText(/Yüklenen: 2 · Sevk: 2 · Teslim: 1/)).toBeTruthy()
    expect(within(reopenedPanel).getByText(/1\. satır · 5 → 3 · Approved/)).toBeTruthy()
    expect(within(reopenedPanel).queryByRole('button', { name: 'Onayla' })).toBeNull()
  })

  it('keeps the same key and frozen body after reply loss, remount and a fresh detail version', async () => {
    const calls: { key: string | null; body: unknown }[] = []
    let loseReply = true
    vi.stubGlobal('fetch', vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if ((init?.method ?? 'GET').toUpperCase() === 'GET') {
        const current = detail()
        return new Response(JSON.stringify(remainderRead(current.lines[0].lineId, current.lines[0].orderUnitId)), { status: 200 })
      }
      const headers = init?.headers as Record<string, string>
      calls.push({ key: headers['Idempotency-Key'] ?? null, body: JSON.parse(String(init?.body)) })
      if (loseReply) {
        loseReply = false
        throw new TypeError('synthetic reply loss')
      }
      return new Response(JSON.stringify({ detail: approvedBody('5'), isReplay: true, originalCorrelationId: 'c-original' }), {
        status: 200, headers: { 'Content-Type': 'application/json' },
      })
    }))

    const binding = '["actor-a","session-1","company-a","draft-a"]'
    const first = detail()
    const firstClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={firstClient}><SalesDraftRemainderChangePanel detail={first} companyId={first.draft.companyId}
      namespace="test-company" actorId="actor-a" routeEntryGeneration={1} attemptBinding={binding}
      canRequest canDecide={false} onRecorded={vi.fn()} /></QueryClientProvider>)
    const firstPanel = await screen.findByTestId('sales-remainder-changes')
    fireEvent.change(within(firstPanel).getByLabelText('İşlem'), { target: { value: 'Revise' } })
    fireEvent.change(within(firstPanel).getByLabelText('Yeni sipariş miktarı'), { target: { value: '3' } })
    fireEvent.change(within(firstPanel).getByLabelText('Gerekçe'), { target: { value: 'SENTETIK yanıt kaybı' } })
    fireEvent.click(within(firstPanel).getByRole('button', { name: 'Onaya gönder' }))
    expect(await within(firstPanel).findByRole('button', { name: 'Aynı işlemi tekrar dene' })).toBeTruthy()

    cleanup()
    const recorded = vi.fn()
    const reopened = detail('5')
    const secondClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={secondClient}><SalesDraftRemainderChangePanel detail={reopened} companyId={reopened.draft.companyId}
      namespace="test-company" actorId="actor-a" routeEntryGeneration={2} attemptBinding={binding}
      canRequest canDecide={false} onRecorded={recorded} /></QueryClientProvider>)
    const reopenedPanel = await screen.findByTestId('sales-remainder-changes')
    fireEvent.click(await within(reopenedPanel).findByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(recorded).toHaveBeenCalledTimes(1))
    expect(calls).toHaveLength(2)
    expect(calls[1]).toEqual(calls[0])
    expect(calls[1].body).toMatchObject({ expectedVersion: '4', quantity: '3' })
  })

  it('does not carry a pending action into another binding and an authority revoke cannot resend it', async () => {
    const current = detail()
    let posts = 0
    vi.stubGlobal('fetch', vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if ((init?.method ?? 'GET').toUpperCase() === 'GET') {
        return new Response(JSON.stringify(remainderRead(current.lines[0].lineId, current.lines[0].orderUnitId)), { status: 200 })
      }
      posts++
      throw new TypeError('synthetic reply loss')
    }))
    const original = '["actor-a","session-1","company-a","draft-a"]'
    const renderPanel = (binding: string, canRequest: boolean) => {
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      return render(<QueryClientProvider client={client}><SalesDraftRemainderChangePanel detail={current} companyId={current.draft.companyId}
        namespace="test-company" actorId="actor-a" routeEntryGeneration={1} attemptBinding={binding}
        canRequest={canRequest} canDecide={false} onRecorded={vi.fn()} /></QueryClientProvider>)
    }
    renderPanel(original, true)
    const panel = await screen.findByTestId('sales-remainder-changes')
    fireEvent.change(within(panel).getByLabelText('Gerekçe'), { target: { value: 'SENTETIK bağ kontrolü' } })
    fireEvent.click(within(panel).getByRole('button', { name: 'Onaya gönder' }))
    expect(await within(panel).findByRole('button', { name: 'Aynı işlemi tekrar dene' })).toBeTruthy()
    cleanup()

    renderPanel('["actor-b","session-1","company-a","draft-a"]', true)
    const other = await screen.findByTestId('sales-remainder-changes')
    expect(within(other).queryByRole('button', { name: 'Aynı işlemi tekrar dene' })).toBeNull()
    cleanup()

    renderPanel(original, false)
    const revoked = await screen.findByTestId('sales-remainder-changes')
    expect(await within(revoked).findByRole('button', { name: 'Aynı işlemi tekrar dene' })).toBeDisabled()
    expect(posts).toBe(1)
  })
})


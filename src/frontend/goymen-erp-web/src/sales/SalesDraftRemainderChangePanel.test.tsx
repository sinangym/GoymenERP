import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import detailRaw from './examples/detail-response.json?raw'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { SalesDraftRemainderChangePanel } from './SalesDraftRemainderChangePanel'

const authBoundary = vi.hoisted(() => ({ capture: vi.fn(() => ({})), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => authBoundary }))

function approvedBody(version = '4'): Record<string, any> {
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
  body.lines[0].quantity = '5'
  body.lines[0].requestedDeliverySlices = null
  return body
}

function detail(): DraftDetail {
  const body = approvedBody()
  const decoded = decodeDraftDetail(body, body.draft.id as string)
  if (decoded === null) throw new Error('fixture must decode')
  return decoded
}

const remainderRead = (lineId: string, unitId: string) => ({
  draftVersion: '4', floorStatus: 'Readable',
  lines: [{ lineId, lineNo: 1, orderUnitId: unitId, quantity: '5', remainder: 'Open', loaded: '2', shipped: '2', delivered: '1', minimumQuantity: '2' }],
  items: [],
})

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

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
})


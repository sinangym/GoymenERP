import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SupplierRevisionPanel } from './SupplierRevisionPanel'
import { fetchSupplierRevisions, sendSupplierRevision, supplierRevisionAttemptKey } from './supplierRevisionApi'
import { fetchPurchasingSupplierOptions } from './api'
import { fetchPurchaseOrderFutureAllocations } from './allocationLinkApi'
import { SUPPLIER_REVISION_TEXT as T } from './supplierRevisionText'
import type { PurchaseOrderSummary, PurchaseOrderLine } from './contract'
import type { ViewOutcome } from '../shell/uiState'
import type { SupplierRevisionHistory } from './supplierRevisionApi'

const scenario = vi.hoisted(() => ({ report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: scenario.report }) }))
vi.mock('./api', () => ({ fetchPurchasingSupplierOptions: vi.fn() }))
vi.mock('./allocationLinkApi', () => ({ fetchPurchaseOrderFutureAllocations: vi.fn() }))
vi.mock('./supplierRevisionApi', async original => ({ ...await original<typeof import('./supplierRevisionApi')>(), fetchSupplierRevisions: vi.fn(), sendSupplierRevision: vi.fn() }))
const company = '11111111-1111-4111-8111-111111111111'; const orderId = '22222222-2222-4222-8222-222222222222'
const supplier = '33333333-3333-4333-8333-333333333333'; const account = '44444444-4444-4444-8444-444444444444'
const next = '55555555-5555-4555-8555-555555555555'; const lineId = '66666666-6666-4666-8666-666666666666'
const unit = '77777777-7777-4777-8777-777777777777'; const at = '2026-11-17T09:00:00Z'; const actor = 'SENTETIK'
const order: PurchaseOrderSummary = { id: orderId, companyId: company, version: '3', status: 'Approved', supplierPartyId: supplier,
  companyAccountId: account, sourceRequestId: null, effectiveOn: '2026-11-17', createdAtUtc: at, updatedAtUtc: at, lineCount: 1 }
const line: PurchaseOrderLine = { lineId, lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null,
  productId: next, productVersion: '1', unitId: unit, ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10', cancelled: '0' }
const clients: QueryClient[] = []
const empty: SupplierRevisionHistory = { companyId: company, orderId, version: '3', revision: 0, revisions: [] }
function mount(canEdit = true, selected: PurchaseOrderSummary = order) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } }); clients.push(client)
  const onSettled = vi.fn(async () => undefined)
  return { onSettled, ...render(<QueryClientProvider client={client}><SupplierRevisionPanel fenceKey="SENTETIK-context" companyId={company}
    actorId={actor} sessionEpoch={1} order={selected} lines={[line]} canEdit={canEdit} hasCurrency onSettled={onSettled} /></QueryClientProvider>) }
}
beforeEach(() => {
  sessionStorage.clear(); vi.resetAllMocks(); scenario.report.mockReset()
  vi.mocked(fetchSupplierRevisions).mockResolvedValue({ type: 'ok-single', data: empty })
  vi.mocked(fetchPurchasingSupplierOptions).mockResolvedValue({ type: 'ok-single', data: { items: [{ supplierPartyId: next, companyAccountId: account, label: 'SENTETIK tedarikçi' }], nextCursor: null } })
  vi.mocked(sendSupplierRevision).mockResolvedValue({ type: 'unknown' })
  vi.spyOn(crypto, 'randomUUID').mockReturnValue('88888888-8888-4888-8888-888888888888')
})
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks() })

describe('Supplier revision screen states and replay', () => {
  it('loads only when opened and shows empty history without editing authority', async () => {
    mount(false); expect(fetchSupplierRevisions).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: T.open }))
    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.getByText(T.denied)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.submit })).toBeNull()
    expect(sendSupplierRevision).not.toHaveBeenCalled()
  })
  it.each<[ViewOutcome<SupplierRevisionHistory>, string]>([
    [{ type: 'offline' }, 'offline'], [{ type: 'network-error' }, 'network-error'], [{ type: 'malformed' }, 'malformed-response'],
    [{ type: 'http', status: 503, body: {} }, 'service-unavailable'], [{ type: 'http', status: 403, body: {} }, 'forbidden'],
    [{ type: 'http', status: 404, body: {} }, 'not-found'], [{ type: 'http', status: 409, body: {} }, 'conflict'],
  ])('keeps source failure %s outside the command form', async (outcome, state) => {
    vi.mocked(fetchSupplierRevisions).mockResolvedValue(outcome); const view = mount()
    await userEvent.click(screen.getByRole('button', { name: T.open }))
    await waitFor(() => expect(view.container.querySelector(`[data-state="${state}"]`)).not.toBeNull())
    expect(screen.queryByRole('button', { name: T.submit })).toBeNull(); expect(sendSupplierRevision).not.toHaveBeenCalled()
  })
  it('shows loading without enabling a command', async () => {
    vi.mocked(fetchSupplierRevisions).mockImplementation(() => new Promise(() => undefined)); const view = mount()
    await userEvent.click(screen.getByRole('button', { name: T.open }))
    expect(view.container.querySelector('[data-state="initial-loading"]')).not.toBeNull()
    expect(sendSupplierRevision).not.toHaveBeenCalled()
  })
  it('freezes the exact payload after an unknown result and retries with the same key', async () => {
    const view = mount(); await userEvent.click(screen.getByRole('button', { name: T.open }))
    await screen.findByText(T.empty); await userEvent.click(screen.getByRole('button', { name: T.search }))
    await screen.findByRole('option', { name: 'SENTETIK tedarikçi' })
    await userEvent.selectOptions(screen.getByLabelText(T.supplier), `${next}|${account}`)
    await userEvent.type(screen.getByLabelText(T.reason), 'SENTETIK correction')
    await userEvent.click(screen.getByRole('button', { name: T.submit }))
    await screen.findByText(T.unknown)
    const first = vi.mocked(sendSupplierRevision).mock.calls[0][0]
    expect(first.command).toEqual({ kind: 'supplier', expectedVersion: '3', supplierPartyId: next, companyAccountId: account, reason: 'SENTETIK correction' })
    expect((screen.getByRole('button', { name: T.submit }) as HTMLButtonElement).closest('fieldset')?.disabled).toBe(true)
    vi.mocked(sendSupplierRevision).mockResolvedValue({ type: 'applied' })
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(view.onSettled).toHaveBeenCalled())
    expect(vi.mocked(sendSupplierRevision).mock.calls[1][0]).toEqual(first)
    expect(sessionStorage.getItem(supplierRevisionAttemptKey(1, actor, company, orderId))).toBeNull()
  })
  it.each([401, 403])('keeps the same key when the retry of an unknown attempt is answered %s', async status => {
    const view = mount(); await userEvent.click(screen.getByRole('button', { name: T.open }))
    await screen.findByText(T.empty); await userEvent.click(screen.getByRole('button', { name: T.search }))
    await screen.findByRole('option', { name: 'SENTETIK tedarikçi' })
    await userEvent.selectOptions(screen.getByLabelText(T.supplier), `${next}|${account}`)
    await userEvent.type(screen.getByLabelText(T.reason), 'SENTETIK correction')
    await userEvent.click(screen.getByRole('button', { name: T.submit }))
    await screen.findByText(T.unknown)
    const first = vi.mocked(sendSupplierRevision).mock.calls[0][0]; const storageKey = supplierRevisionAttemptKey(1, actor, company, orderId)
    vi.mocked(sendSupplierRevision).mockResolvedValue({ type: 'failed', status, code: null })
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(status, expect.anything()))
    expect(JSON.parse(sessionStorage.getItem(storageKey)!)).toEqual(first)
    expect(screen.getByText(T.unknown)).toBeTruthy(); expect(view.onSettled).not.toHaveBeenCalled()
    vi.mocked(sendSupplierRevision).mockResolvedValue({ type: 'applied' })
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(view.onSettled).toHaveBeenCalled())
    expect(vi.mocked(sendSupplierRevision).mock.calls[2][0]).toEqual(first)
    expect(sessionStorage.getItem(storageKey)).toBeNull()
  })
  it('keeps the restored key when its first resend after a reload is answered 403, and drops it on a decided owner refusal', async () => {
    const storageKey = supplierRevisionAttemptKey(1, actor, company, orderId)
    const held = { actorId: actor, companyId: company, orderId, idempotencyKey: '99999999-9999-4999-8999-999999999999',
      command: { kind: 'supplier', expectedVersion: '3', supplierPartyId: next, companyAccountId: account, reason: 'SENTETIK correction' } }
    sessionStorage.setItem(storageKey, JSON.stringify(held))
    vi.mocked(sendSupplierRevision).mockResolvedValue({ type: 'failed', status: 403, code: null })
    mount(); await userEvent.click(screen.getByRole('button', { name: T.open }))
    await userEvent.click(await screen.findByRole('button', { name: T.retry }))
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(403, expect.anything()))
    expect(JSON.parse(sessionStorage.getItem(storageKey)!)).toEqual(held)
    // Only a decided owner answer (here a coded 409) settles the attempt and drops its key.
    vi.mocked(sendSupplierRevision).mockResolvedValue({ type: 'failed', status: 409, code: 'purchasing_version_conflict' })
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(sessionStorage.getItem(storageKey)).toBeNull())
  })
  it('ALS-D126: a 409 for active allocations shows the affected allocations and the confirmation resends exactly that set with a new key', async () => {
    const allocation = '99999999-9999-4999-8999-999999999999'
    vi.mocked(fetchPurchaseOrderFutureAllocations).mockResolvedValue({ type: 'ok-single', data: { orderId, orderVersion: '3', links: [{
      allocationId: allocation, version: '2', lineId, salesOrderId: next, salesOrderLineId: unit, unitId: unit,
      allocatedQuantity: '4', remainingQuantity: '3.5', createdAtUtc: at }] } })
    vi.mocked(sendSupplierRevision).mockResolvedValueOnce({ type: 'failed', status: 409, code: 'purchasing_line_has_allocations' })
      .mockResolvedValueOnce({ type: 'applied' })
    vi.mocked(crypto.randomUUID).mockReturnValueOnce('88888888-8888-4888-8888-888888888888').mockReturnValueOnce('99999999-8888-4888-8888-888888888888')
    const view = mount(); await userEvent.click(screen.getByRole('button', { name: T.open }))
    await screen.findByText(T.empty); await userEvent.click(screen.getByRole('button', { name: T.search }))
    await screen.findByRole('option', { name: 'SENTETIK tedarikçi' })
    await userEvent.selectOptions(screen.getByLabelText(T.supplier), `${next}|${account}`)
    await userEvent.type(screen.getByLabelText(T.reason), 'SENTETIK correction')
    await userEvent.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByText(T.allocationLine(1, '3.5'))).toBeTruthy()
    expect(view.onSettled).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: T.allocationsConfirm }))
    await waitFor(() => expect(view.onSettled).toHaveBeenCalled())
    const [first, second] = vi.mocked(sendSupplierRevision).mock.calls.map(call => call[0])
    expect(first.command).toEqual({ kind: 'supplier', expectedVersion: '3', supplierPartyId: next, companyAccountId: account, reason: 'SENTETIK correction' })
    expect(second.command).toEqual({ ...first.command, seenAllocations: [{ allocationId: allocation, expectedAllocationVersion: '2' }] })
    expect(second.idempotencyKey).not.toBe(first.idempotencyKey)
  })
  it('ALS-D126: an allocation list for another order version is never confirmed', async () => {
    vi.mocked(fetchPurchaseOrderFutureAllocations).mockResolvedValue({ type: 'ok-single', data: { orderId, orderVersion: '4', links: [] } })
    vi.mocked(sendSupplierRevision).mockResolvedValue({ type: 'failed', status: 409, code: 'purchasing_line_has_allocations' })
    mount(); await userEvent.click(screen.getByRole('button', { name: T.open }))
    await screen.findByText(T.empty); await userEvent.click(screen.getByRole('button', { name: T.search }))
    await screen.findByRole('option', { name: 'SENTETIK tedarikçi' })
    await userEvent.selectOptions(screen.getByLabelText(T.supplier), `${next}|${account}`)
    await userEvent.type(screen.getByLabelText(T.reason), 'SENTETIK correction')
    await userEvent.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByText(T.allocationsUnavailable)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.allocationsConfirm })).toBeNull()
    expect(sendSupplierRevision).toHaveBeenCalledTimes(1)
  })
  it('does not send a command when persisted pending data is corrupt', async () => {
    sessionStorage.setItem(supplierRevisionAttemptKey(1, actor, company, orderId), '{'); mount()
    await userEvent.click(screen.getByRole('button', { name: T.open }))
    expect(await screen.findByText(T.pendingStorage)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.submit }) as HTMLButtonElement).closest('fieldset')?.disabled).toBe(true)
    expect(sendSupplierRevision).not.toHaveBeenCalled()
  })
  it('blocks editing when the history version is newer than the displayed order', async () => {
    vi.mocked(fetchSupplierRevisions).mockResolvedValue({ type: 'ok-single', data: { ...empty, version: '4' } }); mount()
    await userEvent.click(screen.getByRole('button', { name: T.open }))
    expect(await screen.findByText(T.stale)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.submit }) as HTMLButtonElement).closest('fieldset')?.disabled).toBe(true)
    expect(sendSupplierRevision).not.toHaveBeenCalled()
  })
})

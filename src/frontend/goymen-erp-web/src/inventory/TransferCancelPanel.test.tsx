import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TransferCancelPanel } from './TransferCancelPanel'
import { cancelTransferDispatch, type TransferCancelOutcome } from './transferCancelApi'
import type { TransferDetail } from './transferContract'
import { TRANSFER_TEXT as T } from './transferText'

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('./transferCancelApi', () => ({ cancelTransferDispatch: vi.fn() }))
const company = '11111111-1111-4111-8111-111111111111'
const view: TransferDetail = { transferId: '22222222-2222-4222-8222-222222222222', companyId: company, version: '2', status: 'InTransit',
  sourceLocationId: '33333333-3333-4333-8333-333333333333', destinationLocationId: '44444444-4444-4444-8444-444444444444',
  sourceCustodianKind: 'Company', sourceCustodianId: company, destinationCustodianKind: 'Company', destinationCustodianId: company,
  plannedAtUtc: '2026-11-16T09:00:00Z', dispatchedAtUtc: '2026-11-16T10:00:00Z', lines: [] }
const applied: TransferCancelOutcome = { type: 'applied', transfer: { ...view, status: 'DispatchCancelled', version: '3' }, replayed: false }
const send = vi.mocked(cancelTransferDispatch)
const mount = (onChanged = vi.fn(), binding: string | null = 'SENTETIK-session-A', canCancel = true, transfer = view) =>
  render(<TransferCancelPanel transfer={transfer} binding={binding} canCancel={canCancel} onChanged={onChanged} />)
async function submit() {
  await userEvent.type(screen.getByLabelText(T.cancelReason), 'SENTETIK iptal gerekçesi')
  fireEvent.click(screen.getByRole('button', { name: T.cancelSubmit }))
}
beforeEach(() => { sessionStorage.clear(); vi.clearAllMocks(); send.mockReset(); send.mockResolvedValue(applied) })
afterEach(() => { cleanup(); sessionStorage.clear(); vi.restoreAllMocks() })

describe('transfer cancel panel, SENTETIK NOT_ENV', () => {
  it('mounts a write only for its published existing cancel capability and an authenticated session binding', () => {
    const page = mount(vi.fn(), 'SENTETIK-session-A', false)
    expect(screen.queryByRole('button', { name: T.cancelSubmit })).toBeNull()
    page.unmount(); mount(vi.fn(), null)
    expect(screen.getByText(T.cancelStorageUnavailable)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.cancelSubmit })).toBeNull()
    expect(send).not.toHaveBeenCalled()
  })
  it('submits only the selected transfer, observed version, UTC time and reason and refreshes on success', async () => {
    const changed = vi.fn(); mount(changed)
    await submit()
    await screen.findByText(T.cancelSaved)
    expect(send).toHaveBeenCalledTimes(1)
    expect(send.mock.calls[0][0]).toMatchObject({ companyId: company, transferId: view.transferId,
      payload: { expectedTransferVersion: '2', reason: 'SENTETIK iptal gerekçesi' } })
    expect(send.mock.calls[0][0].payload.cancelledAtUtc).toMatch(/Z$/)
    expect(changed).toHaveBeenCalledTimes(1)
  })
  it('retains an unknown outcome across a reopen and capability refresh, retries only by user action with the exact original key and payload', async () => {
    send.mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' })
      .mockResolvedValueOnce({ ...applied, replayed: true })
    const page = mount(); await submit(); await screen.findByText(T.cancelUnknown)
    const original = send.mock.calls[0][0]
    page.unmount(); mount(vi.fn(), 'SENTETIK-session-A', true, { ...view, version: '9', status: 'DispatchCancelled' })
    expect(screen.getByText(T.cancelUnknown)).toBeTruthy()
    expect(send).toHaveBeenCalledTimes(1)
    await userEvent.click(screen.getByRole('button', { name: T.cancelRetry }))
    await waitFor(() => expect(send).toHaveBeenCalledTimes(2))
    expect(send.mock.calls[1][0]).toEqual(original)
    // A refreshed cancelled view can hide the settled panel, but the replay was sent with the original observed version.
    expect(send.mock.calls[1][0].payload.expectedTransferVersion).toBe('2')
  })
  it('does not transmit another actor or server session pending command', async () => {
    send.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const page = mount(); await submit(); await screen.findByText(T.cancelUnknown)
    page.unmount(); mount(vi.fn(), 'SENTETIK-session-B')
    expect(screen.queryByText(T.cancelUnknown)).toBeNull()
    expect(send).toHaveBeenCalledTimes(1)
  })
  it('keeps a 403 unresolved command and reports the captured boundary without exposing success', async () => {
    send.mockResolvedValue({ type: 'failed', status: 403, code: null })
    mount(); await submit()
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 }))
    expect(screen.getByRole('button', { name: T.cancelRetry })).toBeTruthy()
    expect(screen.queryByText(T.cancelSaved)).toBeNull()
  })
  it('shows the server closed-window refusal and refreshes without attempting an owner override', async () => {
    send.mockResolvedValue({ type: 'failed', status: 409, code: 'inventory_transfer_cancel_window_closed' })
    const changed = vi.fn(); mount(changed); await submit()
    await screen.findByText(T.cancelRefusal('inventory_transfer_cancel_window_closed'))
    expect(changed).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('button', { name: T.cancelRetry })).toBeNull()
  })
  it('does not send when its pending transport cannot be persisted', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('SENTETIK storage') })
    mount(); await submit()
    await screen.findByText(T.cancelStorageUnavailable)
    expect(send).not.toHaveBeenCalled()
  })
  it('keeps the original key after a known outcome when storage cannot settle it', async () => {
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('SENTETIK storage') })
    mount(); await submit(); await screen.findByText(T.cancelStorageUnsettled)
    const original = send.mock.calls[0][0]
    await userEvent.click(screen.getByRole('button', { name: T.cancelRetry }))
    await waitFor(() => expect(send).toHaveBeenCalledTimes(2))
    expect(send.mock.calls[1][0]).toEqual(original)
  })
  it('drops a late old-session answer instead of refreshing the new session view', async () => {
    let resolve!: (value: TransferCancelOutcome) => void
    send.mockReturnValue(new Promise<TransferCancelOutcome>(accept => { resolve = accept }))
    const changed = vi.fn(); const page = mount(changed); await submit()
    page.rerender(<TransferCancelPanel transfer={view} binding="SENTETIK-session-B" canCancel onChanged={changed} />)
    await act(async () => resolve(applied))
    expect(changed).not.toHaveBeenCalled()
    expect(screen.queryByText(T.cancelSaved)).toBeNull()
  })
})

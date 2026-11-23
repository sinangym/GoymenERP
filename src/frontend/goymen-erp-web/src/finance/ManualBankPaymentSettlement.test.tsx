import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ShellStateContext } from '../shell/shellContext'
import { initialShellState } from '../shell/shellState'
import { FinancePortProvider } from './financePort'
import { ManualBankPaymentDetailPage } from './ManualBankPaymentPages'
import type { ManualBankPaymentStatus, ManualBankPaymentView } from './manualBankPaymentContract'
import type { FinanceReceiptPort, PortReply } from './port'
import { MANUAL_BANK_PAYMENT_SETTLEMENT_TEXT as TEXT } from './financeText'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const PAYMENT = '22222222-2222-4222-8222-222222222222'
const PARTY = '33333333-3333-4333-8333-333333333333'
const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const BANK = '55555555-5555-4555-8555-555555555555'
const CURRENCY = '66666666-6666-4666-8666-666666666666'
const PROFILE = '77777777-7777-4777-8777-777777777777'
function view(status: ManualBankPaymentStatus, companyId = COMPANY): ManualBankPaymentView {
  return {
    id: PAYMENT, companyId, status, linkedDocument: null,
    preparation: { partyId: PARTY, companyAccountId: ACCOUNT, treasuryAccountId: BANK,
      money: { amount: '1250.50', currencyId: CURRENCY }, profile: { profileId: PROFILE, version: '1' },
      occurredOn: '2026-11-17', valueOn: null, bankReference: 'SENTETIK-external', description: null },
    preparedBy: 'SENTETIK-maker', preparedAtUtc: '2026-11-17T08:00:00Z',
    approvedBy: status === 'Draft' ? null : 'SENTETIK-checker',
    approvedAtUtc: status === 'Draft' ? null : '2026-11-17T09:00:00Z',
    recordedBy: status === 'Recorded' ? 'SENTETIK-recorder' : null,
    recordedAtUtc: status === 'Recorded' ? '2026-11-17T10:00:00Z' : null,
    bankResultConfirmed: status === 'Recorded', version: status === 'Draft' ? '1' : status === 'Approved' ? '2' : '3',
    ...(status === 'Recorded' ? { documentNumber: { number: 'DEK-2026-000001', formatVersion: '1' } } : {}),
  }
}
function success(resource: ManualBankPaymentView): PortReply {
  return { kind: 'body', body: { outcome: 'Succeeded', resource, isReplay: true,
    originalCorrelationId: 'SENTETIK-original', issues: [] } }
}
function deferred() {
  let resolve!: (reply: PortReply) => void
  const promise = new Promise<PortReply>((done) => { resolve = done })
  return { promise, resolve }
}
function page(port: FinanceReceiptPort, options: {
  company?: string; actor?: string; epoch?: number; canRecord?: boolean; canApprove?: boolean
} = {}) {
  return <ShellStateContext.Provider value={{ ...initialShellState, sessionEpoch: options.epoch ?? 1 }}>
    <FinancePortProvider port={port}>
      <ManualBankPaymentDetailPage companyId={options.company ?? COMPANY} actorId={options.actor ?? 'SENTETIK-recorder'}
        paymentId={PAYMENT} canEdit={false} canApprove={options.canApprove ?? false}
        canRecord={options.canRecord ?? true} onOpen={() => undefined} />
    </FinancePortProvider>
  </ShellStateContext.Provider>
}
const found = (resource: ManualBankPaymentView): PortReply => ({ kind: 'body', body: { outcome: 'Found', resource } })

describe('manual bank payment unknown settlement', () => {
  it.each(['no-reply', 'malformed'] as const)('retries a %s record with its exact key and body and then shows the frozen voucher', async (failure) => {
    const record = vi.fn().mockResolvedValueOnce(failure === 'no-reply'
      ? { kind: 'no-reply' } : success(view('Approved')))
      .mockResolvedValueOnce(success(view('Recorded')))
    const port = { manualBankPaymentDetail: vi.fn(async () => found(view('Approved'))), recordManualBankPayment: record } as unknown as FinanceReceiptPort
    render(page(port))
    fireEvent.click(await screen.findByLabelText('Banka sonucu gerçekleşti'))
    fireEvent.click(screen.getByRole('button', { name: 'Kesin kaydet' }))
    await screen.findByText(TEXT.unknown)
    expect(screen.queryByRole('button', { name: 'Kesin kaydet' })).toBeNull()
    const first = record.mock.calls[0]
    fireEvent.click(screen.getByRole('button', { name: TEXT.retry }))
    await screen.findByText('Giden ödeme kesin kaydedildi.')
    expect(record.mock.calls[1][0]).toEqual(first[0])
    expect(record.mock.calls[1][1]).toBe(first[1])
    expect(record.mock.calls[1][2]).toBe(first[2])
    expect(record.mock.calls[1][2]).toEqual({ paymentId: PAYMENT, expectedVersion: '2', bankResultConfirmed: true })
    expect(screen.getByText('DEK-2026-000001')).toBeDefined()
    expect(screen.getByText('SENTETIK-external')).toBeDefined()
  })

  it.each(['record', 'approve'] as const)('keeps the %s attempt on a 503 and retries it with the same key and body', async (operation) => {
    // The HTTP port restates a 503 as a code-less Unavailable (httpPort.ts `outcomeOnly`).
    const unavailable: PortReply = { kind: 'body', body: { outcome: 'Unavailable', resource: null, isReplay: false, originalCorrelationId: '', issues: [] } }
    const send = vi.fn().mockResolvedValueOnce(unavailable).mockResolvedValueOnce(unavailable)
      .mockResolvedValueOnce(success(view(operation === 'record' ? 'Recorded' : 'Approved')))
    const port = { manualBankPaymentDetail: vi.fn(async () => found(view(operation === 'record' ? 'Approved' : 'Draft'))),
      recordManualBankPayment: send, approveManualBankPayment: send } as unknown as FinanceReceiptPort
    render(page(port, { canApprove: operation === 'approve', canRecord: operation === 'record' }))
    const start = operation === 'record' ? 'Kesin kaydet' : 'Onayla'
    if (operation === 'record') fireEvent.click(await screen.findByLabelText('Banka sonucu gerçekleşti'))
    fireEvent.click(await screen.findByRole('button', { name: start }))
    await screen.findByText(TEXT.unknown)
    expect(screen.queryByRole('button', { name: start })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: TEXT.retry }))
    await waitFor(() => expect(send).toHaveBeenCalledTimes(2))
    fireEvent.click(await screen.findByRole('button', { name: TEXT.retry }))
    await screen.findByText(operation === 'record' ? 'Giden ödeme kesin kaydedildi.' : 'Ödeme onaylandı.')
    expect(send).toHaveBeenCalledTimes(3)
    expect(new Set(send.mock.calls.map((call) => call[1])).size).toBe(1)
    expect(send.mock.calls.every((call) => call[2] === send.mock.calls[0][2])).toBe(true)
  })

  it('also retains the original approval attempt instead of minting a new key', async () => {
    const approve = vi.fn().mockResolvedValueOnce({ kind: 'no-reply' }).mockResolvedValueOnce(success(view('Approved')))
    const port = { manualBankPaymentDetail: vi.fn(async () => found(view('Draft'))), approveManualBankPayment: approve } as unknown as FinanceReceiptPort
    render(page(port, { canApprove: true, canRecord: false }))
    fireEvent.click(await screen.findByRole('button', { name: 'Onayla' }))
    await screen.findByText(TEXT.unknown)
    expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: TEXT.retry }))
    await screen.findByText('Ödeme onaylandı.')
    expect(approve.mock.calls[1][1]).toBe(approve.mock.calls[0][1])
    expect(approve.mock.calls[1][2]).toBe(approve.mock.calls[0][2])
  })

  it('ignores a late success after permission loss and retries the same attempt only after reauthorization', async () => {
    const first = deferred()
    const record = vi.fn().mockImplementationOnce(() => first.promise).mockResolvedValueOnce(success(view('Recorded')))
    const port = { manualBankPaymentDetail: vi.fn(async () => found(view('Approved'))), recordManualBankPayment: record } as unknown as FinanceReceiptPort
    const mounted = render(page(port))
    fireEvent.click(await screen.findByLabelText('Banka sonucu gerçekleşti'))
    fireEvent.click(screen.getByRole('button', { name: 'Kesin kaydet' }))
    const original = record.mock.calls[0]
    mounted.rerender(page(port, { canRecord: false }))
    await act(async () => { first.resolve(success(view('Recorded'))); await first.promise })
    await screen.findByText(TEXT.unknown)
    expect(screen.queryByText('DEK-2026-000001')).toBeNull()
    expect(screen.queryByRole('button', { name: TEXT.retry })).toBeNull()
    mounted.rerender(page(port, { canRecord: true }))
    fireEvent.click(await screen.findByRole('button', { name: TEXT.retry }))
    await screen.findByText('Giden ödeme kesin kaydedildi.')
    expect(record.mock.calls[1][1]).toBe(original[1])
    expect(record.mock.calls[1][2]).toBe(original[2])
  })

  it.each(['actor', 'company', 'session'] as const)('drops the old %s context reply and does not expose or reuse its pending attempt', async (change) => {
    const first = deferred()
    const record = vi.fn().mockImplementationOnce(() => first.promise)
      .mockImplementation(async (scope: { companyId: string }) => success(view('Recorded', scope.companyId)))
    const detail = vi.fn(async (scope: { companyId: string }) => found(view('Approved', scope.companyId)))
    const port = { manualBankPaymentDetail: detail, recordManualBankPayment: record } as unknown as FinanceReceiptPort
    const mounted = render(page(port))
    fireEvent.click(await screen.findByLabelText('Banka sonucu gerçekleşti'))
    fireEvent.click(screen.getByRole('button', { name: 'Kesin kaydet' }))
    const original = record.mock.calls[0]
    const next = change === 'actor' ? { actor: 'SENTETIK-next-recorder' }
      : change === 'company' ? { company: OTHER_COMPANY } : { epoch: 2 }
    mounted.rerender(page(port, next))
    await waitFor(() => expect(detail).toHaveBeenCalledTimes(2))
    await act(async () => { first.resolve(success(view('Recorded'))); await first.promise })
    const confirmation = await screen.findByLabelText('Banka sonucu gerçekleşti')
    expect((confirmation as HTMLInputElement).checked).toBe(false)
    expect(screen.queryByText('DEK-2026-000001')).toBeNull()
    expect(screen.queryByRole('button', { name: TEXT.retry })).toBeNull()
    fireEvent.click(confirmation)
    fireEvent.click(screen.getByRole('button', { name: 'Kesin kaydet' }))
    await screen.findByText('Giden ödeme kesin kaydedildi.')
    expect(record.mock.calls[1][1]).not.toBe(original[1])
    expect(record.mock.calls[1][0]).toEqual({ companyId: change === 'company' ? OTHER_COMPANY : COMPANY })
  })
})

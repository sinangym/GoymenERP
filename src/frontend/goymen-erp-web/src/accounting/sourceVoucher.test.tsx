import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { SourceVouchersPage } from './SourceVouchersPage'
import { approveSourceVoucher, decodeSourceVoucher, proposeSourceVoucherReversal } from './sourceVoucherApi'

const state = vi.hoisted(() => ({ company: 'b0438000-0000-4000-8000-000000000001', actor: 'b0438000-0000-4000-8000-000000000002', reversal: false }))
const maker = 'b0438000-0000-4000-8000-000000000003'; const id = 'b0438000-0000-4000-8000-000000000004'
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ routeEntryGeneration: 1,
  activeContext: { companyId: state.company, userId: state.actor, scopeMode: 'single' } }) }))
vi.mock('../shell/shellState', () => ({ contextKey: () => state.company, contextKeyString: (key: string) => key, hasCapability: () => state.reversal }))
vi.mock('./NormalJournalApprovalPeriod', () => ({ NormalJournalApprovalPeriod: ({ value, onChange, disabled }: {
  value: string; onChange: (id: string) => void; disabled: boolean }) => <label>Açık dönem<select value={value} disabled={disabled}
    onChange={event => onChange(event.target.value)}><option value="">Seç</option><option value="b0438000-0000-4000-8000-000000000008">Aralık</option></select></label> }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => 'SENTETIK', report: vi.fn() }) }))
const fixture = (patch: Record<string, unknown> = {}) => ({ id, companyId: state.company, documentKind: 'sales-invoice',
  sourceDocumentId: id, invoiceId: id, finalizedVersion: 4, sourceActorId: maker, partyId: id, documentCurrencyId: id,
  exchangeRate: null, documentDate: '2026-11-17', dueDate: null, status: 'AwaitingApproval', version: 1, mappingVersion: 1,
  periodId: id, journalId: null, approvedBy: null, approvedAtUtc: null, cancellationReason: null, officialInvoiceNo: 'SENTETIK-INV', functionalPayable: '120.01',
  lines: [{ lineNumber: 1, mappingKey: 'receivable', side: 1, accountId: id, functionalAmount: '120.01', sourceLineNumber: null,
    vatRateCode: null, accountCode: 'SENTETIK', accountName: 'SENTETIK hesap' }], ...patch })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const mount = () => render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><SourceVouchersPage mode="approve" /></QueryClientProvider></MemoryRouter>)
afterEach(() => { cleanup(); vi.unstubAllGlobals(); state.actor = 'b0438000-0000-4000-8000-000000000002'; state.reversal = false })

describe('invoice source voucher approval', () => {
  it('preserves exact source values and rejects incomplete or foreign success', () => {
    expect(decodeSourceVoucher(fixture(), state.company)?.functionalPayable).toBe('120.01')
    for (const patch of [{ companyId: maker }, { version: 0 }, { functionalPayable: 120.01 }, { documentDate: '2026-02-31' },
      { status: 'Posted', approvedBy: maker, approvedAtUtc: '2026-11-17T12:00:00Z', journalId: id }, { status: 'Cancelled' }])
      expect(decodeSourceVoucher(fixture(patch), state.company)).toBeNull()
  })
  it('does not offer the source invoice finalizer approval', async () => {
    state.actor = maker
    const fetch = vi.fn(async () => json({ companyId: state.company, items: [fixture()] })); vi.stubGlobal('fetch', fetch)
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Fişi aç' }))
    expect(screen.getByRole('table', { name: 'Fatura fişleri' })).toBeTruthy()
    expect(screen.getByRole('table', { name: 'Fatura fişleri · SENTETIK-INV' })).toBeTruthy()
    expect(await screen.findByText('Kesinleştirdiğiniz faturanın fişini başka bir muhasebe yetkilisi onaylamalı.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onayla ve yevmiyeye geçir' })).toBeNull()
    expect(fetch.mock.calls).toHaveLength(1)
  })
  it('holds an unknown command and retries its original payload and key', async () => {
    const calls: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { calls.push(init); return calls.length === 1 ? json({}, 503)
        : json(fixture({ status: 'Posted', version: 2, approvedBy: state.actor, approvedAtUtc: '2026-11-17T12:00:00Z', journalId: id })) }
      return json({ companyId: state.company, items: [fixture()] })
    }))
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Fişi aç' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Onayla ve yevmiyeye geçir' }))
    const retry = await screen.findByRole('button', { name: 'Aynı komutu yeniden gönder' })
    expect((screen.getByRole('button', { name: 'Fişi aç' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(retry); await waitFor(() => expect(calls).toHaveLength(2))
    expect(calls[0].body).toBe(calls[1].body); expect(calls[0].headers).toEqual(calls[1].headers)
  })
  it('does not turn a malformed 200 into a confirmed journal', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(fixture({ status: 'Posted' }))))
    expect(await approveSourceVoucher(state.company, { id, key: 'SENTETIK-key', expectedVersion: 1 })).toEqual({ type: 'unknown' })
  })
})

const cancellation = (patch: Record<string, unknown> = {}) => fixture({ status: 'CancellationReversalPending', version: 2,
  journalId: id, approvedBy: state.actor, approvedAtUtc: '2026-11-17T12:00:00Z', cancellationReason: 'SENTETIK resmî iptal',
  cancellationId: 'b0438000-0000-4000-8000-000000000009', ...patch })
const proposal = (patch: Record<string, unknown> = {}) => ({ proposalId: 'b0438000-0000-4000-8000-000000000007',
  companyId: state.company, sourceJournalId: id, reversalJournalId: null, periodId: 'b0438000-0000-4000-8000-000000000008',
  transactionDate: '2026-12-02', accountingDate: '2026-12-03', valueDate: null, reason: 'SENTETIK ters kayıt', proposedBy: state.actor,
  proposedAtUtc: '2026-11-17T13:00:00Z', status: 'Pending', decidedBy: null, decidedAtUtc: null, decisionReason: null, version: 1, ...patch })
describe('source invoice cancellation reversal proposal', () => {
  it('rejects another source journal, missing cancellation proof and fabricated approved status', () => {
    expect(decodeSourceVoucher(cancellation({ reversalProposal: proposal() }), state.company)?.reversalProposal?.status).toBe('Pending')
    for (const patch of [{ reversalProposal: proposal({ sourceJournalId: maker }) }, { reversalProposal: proposal(), cancellationId: null },
      { status: 'CancelledByReversal', reversalProposal: proposal() }, { status: 'CancelledByReversal' }])
      expect(decodeSourceVoucher(cancellation(patch), state.company)).toBeNull()
  })
  it('never offers a reversal proposal without the existing reversal capability', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: state.company, items: [cancellation()] })))
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Fişi aç' }))
    expect(await screen.findByText('Ters kayıt hazırlama yetkisi bulunmuyor.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Ters kayıt önerisini kaydet' })).toBeNull()
  })
  it('freezes selected period and dates after an unknown result and retries the identical proposal', async () => {
    state.reversal = true; const calls: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        calls.push(init); const request = JSON.parse(init.body as string)
        return calls.length === 1 ? json({}, 503) : json(cancellation({ version: 3, reversalProposal: proposal({
          proposalId: request.proposalId, periodId: request.periodId, transactionDate: request.transactionDate,
          accountingDate: request.accountingDate, valueDate: request.valueDate, reason: request.reason }) }))
      }
      return json({ companyId: state.company, items: [cancellation()] })
    }))
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Fişi aç' }))
    fireEvent.change(await screen.findByLabelText('İşlem tarihi'), { target: { value: '2026-12-02' } })
    fireEvent.change(screen.getByLabelText('Muhasebe tarihi'), { target: { value: '2026-12-03' } })
    fireEvent.change(screen.getByLabelText('Açık dönem'), { target: { value: 'b0438000-0000-4000-8000-000000000008' } })
    fireEvent.change(screen.getByLabelText('Ters kayıt gerekçesi'), { target: { value: 'SENTETIK ters kayıt' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ters kayıt önerisini kaydet' }))
    const retry = await screen.findByRole('button', { name: 'Aynı komutu yeniden gönder' })
    expect((screen.getByRole('button', { name: 'Fişi aç' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('Muhasebe tarihi') as HTMLInputElement).disabled).toBe(true)
    fireEvent.click(retry); await screen.findByText('Ters kayıt önerisi kaydedildi; ayrı onay bekliyor.')
    expect(calls).toHaveLength(2); expect(calls[0].body).toBe(calls[1].body); expect(calls[0].headers).toEqual(calls[1].headers)
    expect(screen.queryByRole('button', { name: 'Ters kayıt önerisini kaydet' })).toBeNull()
  })
  it('treats a changed successful selection as unknown rather than a confirmed proposal', async () => {
    const body = proposal(); vi.stubGlobal('fetch', vi.fn(async () => json(cancellation({ version: 3, reversalProposal: body }))))
    expect(await proposeSourceVoucherReversal(state.company, { id, key: 'SENTETIK', request: { expectedVersion: 2,
      proposalId: body.proposalId, periodId: body.periodId, transactionDate: '2026-12-02', accountingDate: '2026-12-04', valueDate: null,
      reason: body.reason } })).toEqual({ type: 'unknown' })
  })
})

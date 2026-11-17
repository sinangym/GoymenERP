import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SourceVouchersPage } from './SourceVouchersPage'
import { approveSourceVoucher, decodeSourceVoucher } from './sourceVoucherApi'

const state = vi.hoisted(() => ({ company: 'b0438000-0000-4000-8000-000000000001', actor: 'b0438000-0000-4000-8000-000000000002' }))
const maker = 'b0438000-0000-4000-8000-000000000003'; const id = 'b0438000-0000-4000-8000-000000000004'
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ routeEntryGeneration: 1,
  activeContext: { companyId: state.company, userId: state.actor, scopeMode: 'single' } }) }))
vi.mock('../shell/shellState', () => ({ contextKey: () => state.company, contextKeyString: (key: string) => key }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => 'SENTETIK', report: vi.fn() }) }))
const fixture = (patch: Record<string, unknown> = {}) => ({ id, companyId: state.company, documentKind: 'sales-invoice',
  sourceDocumentId: id, invoiceId: id, finalizedVersion: 4, sourceActorId: maker, partyId: id, documentCurrencyId: id,
  exchangeRate: null, documentDate: '2026-11-17', dueDate: null, status: 'AwaitingApproval', version: 1, mappingVersion: 1,
  periodId: id, journalId: null, approvedBy: null, approvedAtUtc: null, cancellationReason: null, officialInvoiceNo: 'SENTETIK-INV', functionalPayable: '120.01',
  lines: [{ lineNumber: 1, mappingKey: 'receivable', side: 1, accountId: id, functionalAmount: '120.01', sourceLineNumber: null,
    vatRateCode: null, accountCode: 'SENTETIK', accountName: 'SENTETIK hesap' }], ...patch })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const mount = () => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><SourceVouchersPage mode="approve" /></QueryClientProvider>)
afterEach(() => { cleanup(); vi.unstubAllGlobals(); state.actor = 'b0438000-0000-4000-8000-000000000002' })

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

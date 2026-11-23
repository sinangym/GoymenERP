// @vitest-environment jsdom
// SENTETIK / NOT_ENV: component evidence over mocked transport, not SQL or browser acceptance.
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { PartyBalanceTransferPage } from './PartyBalanceTransferPage'
import { BALANCE_TRANSFER_TEXT as T } from './PartyBalanceTransferText'
import { COMPANY_A, renderFinance, shellFor } from './testHarness'
import { fetchBalanceTransferPreview, recordBalanceTransfer } from './PartyBalanceTransferHttp'

const id = (n: number) => `${n.toString().padStart(8, '0')}-2222-4222-8222-222222222222`
const oldParty = id(1), oldAccount = id(2), newParty = id(3), newAccount = id(4), documentId = id(5), currency = id(6)

vi.mock('../cari/api', () => ({
  fetchCounterparties: vi.fn(async (_scope: unknown, query: { partyState: string }) => ({ type: 'ok-single', data: {
    items: query.partyState === 'all'
      ? [{ id: oldParty, code: 'ESKI', legalName: 'SENTETIK eski', isActive: false,
        companyAccounts: [{ id: oldAccount, companyId: COMPANY_A, isActive: false }] }]
      : [{ id: newParty, code: 'YENI', legalName: 'SENTETIK yeni', isActive: true,
        companyAccounts: [{ id: newAccount, companyId: COMPANY_A, isActive: true }] }],
    nextCursor: null } })),
}))
vi.mock('../documents/api', () => ({
  documentsQueryKey: (...parts: unknown[]) => ['documents', ...parts],
  fetchDocuments: vi.fn(async () => ({ type: 'ok-single', data: { items: [
    { id: documentId, downloadable: true, fileName: 'SENTETIK.pdf', versionNo: 1, sha256: 'a'.repeat(64) }] } })),
}))
vi.mock('../documents/DocumentsPanel', () => ({ DocumentsPanel: () => null }))
vi.mock('./currencyCodes', () => ({ WithCurrencyCodes: ({ children }: { children: (codes: unknown) => unknown }) => children(null),
  currencyShown: (value: string) => value }))
vi.mock('./PartyBalanceTransferHttp', () => ({ fetchBalanceTransferPreview: vi.fn(), recordBalanceTransfer: vi.fn() }))

afterEach(() => { cleanup(); vi.clearAllMocks() })

describe('party balance transfer unknown attempt (K3 P2-1)', () => {
  it('leaves an unknown transfer only through an explicit same-key final check that gets a decided answer', async () => {
    vi.mocked(fetchBalanceTransferPreview).mockResolvedValue({ type: 'ok-single', data: {
      companyId: COMPANY_A, snapshotVersion: '7', openItemCount: 0, receiptCount: 0, paymentCount: 0, canRecord: true,
      currencies: [{ currencyId: currency, side: 'Debit', amount: '10.00', openReceivable: '10.00', openPayable: '0.00' }],
      ownerProof: { companyId: COMPANY_A, predecessorPartyId: oldParty, predecessorAccountId: oldAccount, successorPartyId: newParty,
        successorAccountId: newAccount, predecessorPartyVersion: '1', predecessorAccountVersion: '1', predecessorCode: 'ESKI',
        predecessorName: 'SENTETIK eski', successorPartyVersion: '1', successorAccountVersion: '1', successorCode: 'YENI',
        successorName: 'SENTETIK yeni', linkKind: 'Succession', effectiveOn: null, legalBasis: null, linkRecordedAtUtc: null },
    } } as never)
    vi.mocked(recordBalanceTransfer)
      .mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'refused', status: 404 })
      .mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'refused', status: 404 })
    renderFinance(<PartyBalanceTransferPage canRecord />, { port: null, shell: shellFor(COMPANY_A), router: true })

    await screen.findByRole('option', { name: 'ESKI — SENTETIK eski' })
    await screen.findByRole('option', { name: 'YENI — SENTETIK yeni' })
    fireEvent.change(screen.getByRole('combobox', { name: T.predecessor }), { target: { value: oldAccount } })
    fireEvent.change(screen.getByRole('combobox', { name: T.successor }), { target: { value: newAccount } })
    fireEvent.click(screen.getByRole('button', { name: T.preview }))
    await screen.findByText(/ESKI — SENTETIK eski → YENI — SENTETIK yeni/)
    fireEvent.change(await screen.findByLabelText(T.evidence), { target: { value: documentId } })
    fireEvent.change(screen.getByLabelText(T.occurredOn), { target: { value: '2026-11-17' } })
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK devir' } })
    fireEvent.click(screen.getByRole('button', { name: T.submit }))
    await screen.findByText(T.unknown)

    // An ordinary retry answered by a non-replayed refusal cannot settle the lost first send.
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(recordBalanceTransfer).toHaveBeenCalledTimes(2))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).disabled).toBe(true)

    // The final check without an answer keeps the attempt open.
    fireEvent.click(screen.getByRole('button', { name: T.finalCheck }))
    await screen.findByText(T.finalOpen)
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).disabled).toBe(true)

    // A decided answer to the final check closes it and releases the form.
    fireEvent.click(screen.getByRole('button', { name: T.finalCheck }))
    await screen.findByText(T.abandoned)
    expect(screen.queryByRole('button', { name: T.finalCheck })).toBeNull()
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).disabled).toBe(false)

    const calls = vi.mocked(recordBalanceTransfer).mock.calls
    expect(calls).toHaveLength(4)
    expect(new Set(calls.map((call) => call[1])).size).toBe(1)
    expect(calls.every((call) => call[2] === calls[0][2])).toBe(true)
  })
})

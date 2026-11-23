// @vitest-environment jsdom
// SENTETIK / NOT_ENV: FNS-D121 panel over mocked transport.
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CariPartyIbanPanel } from './CariPartyIbanPanel'
import { PARTY_IBAN_TEXT as T } from './partyIbanText'
import { addPartyIban, fetchPartyIbans } from './partyIbanApi'

vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: vi.fn(() => ({})), report: vi.fn() }) }))
vi.mock('./partyIbanApi', () => ({ fetchPartyIbans: vi.fn(), addPartyIban: vi.fn(), deactivatePartyIban: vi.fn() }))

const company = 'aaaaaaaa-0000-4000-8000-000000000001'
const party = '11111111-1111-4111-8111-111111111111'
const row = { id: '22222222-2222-4222-8222-222222222222', companyId: company, partyId: party, iban: 'TR130006200000000000000002',
  bankName: 'SENTETIK Banka', description: null, isActive: true, version: '1', addedBy: 'SENTETIK', addedAtUtc: '2026-11-23T08:00:00+00:00',
  deactivatedBy: null, deactivatedAtUtc: null, deactivationReason: null }

function renderPanel(canManage = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  return render(<QueryClientProvider client={client}><CariPartyIbanPanel partyId={party} companyId={company} namespace="ns" canManage={canManage} /></QueryClientProvider>)
}
afterEach(() => { cleanup(); vi.clearAllMocks() })

describe('cari IBAN panel', () => {
  it('lists the IBANs grouped and shows the empty and forbidden states', async () => {
    vi.mocked(fetchPartyIbans).mockResolvedValueOnce({ type: 'ok', data: { companyId: company, partyId: party, ibans: [row] } })
    renderPanel()
    expect(await screen.findByText('TR13 0006 2000 0000 0000 0000 02')).toBeTruthy()
    cleanup()
    vi.mocked(fetchPartyIbans).mockResolvedValueOnce({ type: 'ok', data: { companyId: company, partyId: party, ibans: [] } })
    renderPanel()
    expect(await screen.findByText(T.empty)).toBeTruthy()
    cleanup()
    vi.mocked(fetchPartyIbans).mockResolvedValueOnce({ type: 'http', status: 403 })
    renderPanel()
    expect(await screen.findByText(T.forbidden)).toBeTruthy()
  })

  it('draws no add form and no deactivate button without the manage control', async () => {
    vi.mocked(fetchPartyIbans).mockResolvedValueOnce({ type: 'ok', data: { companyId: company, partyId: party, ibans: [row] } })
    renderPanel(false)
    expect(await screen.findByText('TR13 0006 2000 0000 0000 0000 02')).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.add })).toBeNull()
    expect(screen.queryByRole('button', { name: T.deactivate })).toBeNull()
    expect(screen.queryByLabelText(T.iban)).toBeNull()
  })

  it('refuses a foreign IBAN with its own Turkish message and sends nothing', async () => {
    vi.mocked(fetchPartyIbans).mockResolvedValue({ type: 'ok', data: { companyId: company, partyId: party, ibans: [] } })
    renderPanel()
    await screen.findByText(T.empty)
    fireEvent.change(screen.getByLabelText(T.iban), { target: { value: 'DE89 3704 0044 0532 0130 00' } })
    fireEvent.change(screen.getByLabelText(T.bank), { target: { value: 'SENTETIK Bank' } })
    fireEvent.click(screen.getByRole('button', { name: T.add }))
    expect(await screen.findByText(T.foreignIban)).toBeTruthy()
    expect(addPartyIban).not.toHaveBeenCalled()
  })

  it('refuses an invalid IBAN locally and retries an unknown add with the same key and body', async () => {
    vi.mocked(fetchPartyIbans).mockResolvedValue({ type: 'ok', data: { companyId: company, partyId: party, ibans: [] } })
    vi.mocked(addPartyIban).mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'answered', result: { outcome: 'Succeeded', resource: row, code: null, replayed: true } })
    renderPanel()
    await screen.findByText(T.empty)
    fireEvent.change(screen.getByLabelText(T.iban), { target: { value: 'TR130006200000000000000003' } })
    fireEvent.change(screen.getByLabelText(T.bank), { target: { value: 'SENTETIK Banka' } })
    fireEvent.click(screen.getByRole('button', { name: T.add }))
    expect(await screen.findByText(T.invalidIban)).toBeTruthy()
    expect(addPartyIban).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText(T.iban), { target: { value: 'TR13 0006 2000 0000 0000 0000 02' } })
    fireEvent.click(screen.getByRole('button', { name: T.add }))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    expect(await screen.findByText(T.added)).toBeTruthy()
    const calls = vi.mocked(addPartyIban).mock.calls
    expect(calls[0][2]).toBe(calls[1][2])
    expect(calls[0][3]).toEqual(calls[1][3])
    expect(calls[0][3].iban).toBe('TR130006200000000000000002')
  })
})

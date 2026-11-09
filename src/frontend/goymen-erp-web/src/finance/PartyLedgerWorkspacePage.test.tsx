/**
 * B-05 (KLN-D3): the party-ledger workspace opened from Muhasebe > Cari Hesaplar and Cari > Cari Hesap Takibi — the
 * account picker, the address that carries the choice, and the existing ledger and balance card underneath.
 * Component evidence only (SENTETIK).
 */

import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PartyLedgerWorkspacePage } from './PartyLedgerWorkspacePage'
import { LEDGER_ACCOUNT, LEDGER_PARTY, ledgerBody } from './partyLedgerFixtures'
import { WORKSPACE_ACCOUNTS_INVALID, WORKSPACE_NO_ACCOUNTS, WORKSPACE_TITLE_CARI } from './partyLedgerText'
import { COMPANY_A, found, recordingPort, renderFinance } from './testHarness'

function counterparties(items: unknown[], status = 200) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({
    items,
    nextCursor: null,
    hasMore: false,
    totalCount: null,
    countScope: null,
  }), { status, headers: { 'Content-Type': 'application/json' } })))
}

const PARTY_ROW = {
  id: LEDGER_PARTY,
  code: 'C-100',
  kind: 1,
  legalName: 'SENTETIK Alici',
  tradeName: null,
  isActive: true,
  version: 1,
  companyAccounts: [{ id: LEDGER_ACCOUNT, companyId: COMPANY_A, roles: 1, isActive: true, version: 1 }],
}

const balance = {
  companyId: COMPANY_A, partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [],
}

afterEach(() => vi.unstubAllGlobals())

describe('party ledger workspace (B-05)', () => {
  it('offers the active party accounts and opens the chosen one in the ledger under the same address', async () => {
    counterparties([PARTY_ROW])
    const { port, reads } = recordingPort((method) => method === 'partyBalance' ? found(balance) : found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/cari/hesap-takibi" title={WORKSPACE_TITLE_CARI} />,
      { port, router: true })

    expect(screen.getByRole('heading', { name: WORKSPACE_TITLE_CARI })).toBeTruthy()
    await screen.findByRole('option', { name: 'C-100 - SENTETIK Alici' })
    // Nothing is read before an account is chosen.
    expect(reads).toEqual([])

    fireEvent.change(screen.getByLabelText('Cari hesabı'), { target: { value: LEDGER_PARTY } })
    await waitFor(() => expect(reads.find((read) => read.method === 'partyBalance')?.request)
      .toEqual({ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT }))
    expect(reads.some((read) => read.method === 'partyLedger')).toBe(true)
    expect((screen.getByLabelText('Cari hesabı') as HTMLSelectElement).value).toBe(LEDGER_PARTY)
  })

  it.each([
    ['no usable account', [], 200, WORKSPACE_NO_ACCOUNTS],
    ['an account list that cannot be read', [], 503, WORKSPACE_ACCOUNTS_INVALID],
  ])('says so when there is %s and reads no ledger', async (_name, items, status, text) => {
    counterparties(items, status)
    const { port, reads } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/muhasebe/cari-hesaplar" title="Cari Hesaplar" />,
      { port, router: true })

    expect(await screen.findByText(text)).toBeTruthy()
    expect(reads).toEqual([])
  })
})

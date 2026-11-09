/**
 * B-05 (KLN-D3): the party-ledger workspace opened from Muhasebe > Cari Hesaplar and Cari > Cari Hesap Takibi — the
 * account picker, the address that carries the choice, and the existing ledger and balance card underneath.
 * Component evidence only (SENTETIK).
 */

import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PartyLedgerWorkspacePage } from './PartyLedgerWorkspacePage'
import { LEDGER_ACCOUNT, LEDGER_PARTY, ledgerBody } from './partyLedgerFixtures'
import {
  WORKSPACE_ACCOUNTS_PARTIAL,
  WORKSPACE_NO_ACCOUNTS,
  WORKSPACE_TITLE_CARI,
} from './partyLedgerText'
import { COMPANY_A, COMPANY_B, found, recordingPort, renderFinance } from './testHarness'

function counterparties(items: unknown[], status = 200) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({
    items,
    nextCursor: null,
    hasMore: false,
    totalCount: null,
    countScope: null,
  }), { status, headers: { 'Content-Type': 'application/json' } })))
}

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))

function pages(responses: readonly { items: unknown[]; nextCursor: string | null; hasMore: boolean }[]) {
  let index = 0
  const fetchMock = vi.fn(async () => {
    const page = responses[Math.min(index, responses.length - 1)]
    index += 1
    return new Response(JSON.stringify({ ...page, totalCount: null, countScope: null }), {
      status: 200, headers: { 'Content-Type': 'application/json' },
    })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function counterpartiesSpy(items: unknown[]) {
  const fetchMock = vi.fn(async (..._args: unknown[]) => new Response(JSON.stringify({
    items, nextCursor: null, hasMore: false, totalCount: null, countScope: null,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
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

afterEach(() => {
  vi.unstubAllGlobals()
  boundary.report.mockClear()
})

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

    fireEvent.change(screen.getByLabelText('Cari hesabı'), { target: { value: `${LEDGER_PARTY}:${LEDGER_ACCOUNT}` } })
    await waitFor(() => expect(reads.find((read) => read.method === 'partyBalance')?.request)
      .toEqual({ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT }))
    expect(reads.some((read) => read.method === 'partyLedger')).toBe(true)
    expect((screen.getByLabelText('Cari hesabı') as HTMLSelectElement).value).toBe(`${LEDGER_PARTY}:${LEDGER_ACCOUNT}`)
  })

  it('says there is no usable account and reads no ledger', async () => {
    counterparties([])
    const { port, reads } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/muhasebe/cari-hesaplar" title="Cari Hesaplar" />,
      { port, router: true })

    expect(await screen.findByText(WORKSPACE_NO_ACCOUNTS)).toBeTruthy()
    expect(reads).toEqual([])
  })

  it.each([
    [401, 'Oturum sonlandı', false],
    [403, 'Yetkiniz yok', false],
    [503, 'Servis geçici olarak kullanılamıyor', true],
  ])('shows a distinct state for an account list answered %i and reports 401/403 to the boundary', async (status, title, retry) => {
    counterparties([], status)
    const { port, reads } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/muhasebe/cari-hesaplar" title="Cari Hesaplar" />,
      { port, router: true })

    expect(await screen.findByText(new RegExp(title))).toBeTruthy()
    expect(Boolean(screen.queryByRole('button', { name: /tekrar/i }))).toBe(retry)
    expect(boundary.report).toHaveBeenCalledTimes(status === 401 || status === 403 ? 1 : 0)
    expect(reads).toEqual([])
  })

  it('says the response is unexpected when the list does not match the contract, and retries', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ items: 'x' }), {
      status: 200, headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)
    const { port } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/muhasebe/cari-hesaplar" title="Cari Hesaplar" />,
      { port, router: true })

    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /tekrar/i }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
  })

  it('fails closed when the company list carries a party with several accounts instead of dropping it', async () => {
    const second = 'dddddddd-0000-4000-8000-000000000004'
    counterparties([{
      ...PARTY_ROW,
      companyAccounts: [PARTY_ROW.companyAccounts[0], { ...PARTY_ROW.companyAccounts[0], id: second, companyId: COMPANY_B }],
    }])
    const { port, reads } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/cari/hesap-takibi" title={WORKSPACE_TITLE_CARI} />,
      { port, router: true })

    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByLabelText('Cari hesabı')).toBeNull()
    expect(reads).toEqual([])
  })

  it('follows the cursor to the next page and says so when more remain beyond the bound', async () => {
    const other = { ...PARTY_ROW, id: 'ffffffff-0000-4000-8000-000000000006', code: 'C-200', legalName: 'SENTETIK Ikinci' }
    const fetchMock = pages([
      { items: [PARTY_ROW], nextCursor: 'c1', hasMore: true },
      { items: [other], nextCursor: null, hasMore: false },
    ])
    const { port } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/cari/hesap-takibi" title={WORKSPACE_TITLE_CARI} />,
      { port, router: true })

    await screen.findByRole('option', { name: 'C-200 - SENTETIK Ikinci' })
    expect(screen.getByRole('option', { name: 'C-100 - SENTETIK Alici' })).toBeTruthy()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(screen.queryByText(WORKSPACE_ACCOUNTS_PARTIAL)).toBeNull()
  })

  it('marks the list partial when the server still has more after the bounded pages', async () => {
    let n = 0
    vi.stubGlobal('fetch', vi.fn(async () => {
      n += 1
      const row = { ...PARTY_ROW, id: `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`, code: `C-${n}` }
      return new Response(JSON.stringify({
        items: [row], nextCursor: `c${n}`, hasMore: true, totalCount: null, countScope: null,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }))
    const { port } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/cari/hesap-takibi" title={WORKSPACE_TITLE_CARI} />,
      { port, router: true })

    expect(await screen.findByText(WORKSPACE_ACCOUNTS_PARTIAL)).toBeTruthy()
    expect(screen.getAllByRole('option').length).toBeGreaterThan(1)
  })

  it('fails closed when a party repeats across pages', async () => {
    pages([
      { items: [PARTY_ROW], nextCursor: 'c1', hasMore: true },
      { items: [PARTY_ROW], nextCursor: null, hasMore: false },
    ])
    const { port } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/cari/hesap-takibi" title={WORKSPACE_TITLE_CARI} />,
      { port, router: true })

    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByLabelText('Cari hesabı')).toBeNull()
  })

  it('reopens the same ledger from the address and reads the current month by default', async () => {
    counterparties([PARTY_ROW])
    const { port, reads } = recordingPort((method) => method === 'partyBalance' ? found(balance) : found(ledgerBody()))
    const page = <PartyLedgerWorkspacePage companyId={COMPANY_A} base="/cari/hesap-takibi" title={WORKSPACE_TITLE_CARI} />
    const view = renderFinance(page, { port, router: true })
    await screen.findByRole('option', { name: 'C-100 - SENTETIK Alici' })
    fireEvent.change(screen.getByLabelText('Cari hesabı'), { target: { value: `${LEDGER_PARTY}:${LEDGER_ACCOUNT}` } })
    await waitFor(() => expect(reads.some((read) => read.method === 'partyLedger')).toBe(true))

    const now = new Date()
    const pad = (value: number) => String(value).padStart(2, '0')
    const month = `${now.getFullYear()}-${pad(now.getMonth() + 1)}`
    const first = reads.find((read) => read.method === 'partyLedger')?.request as { from: string; to: string }
    expect(first.from).toBe(`${month}-01`)
    expect(first.to).toBe(`${month}-${pad(now.getDate())}`)

    // The shell stays; the page is opened again under the same address.
    view.remount(page)
    await waitFor(() => expect((screen.getByLabelText('Cari hesabı') as HTMLSelectElement).value)
      .toBe(`${LEDGER_PARTY}:${LEDGER_ACCOUNT}`))
    await waitFor(() => expect(reads.filter((read) => read.method === 'partyLedger').length).toBeGreaterThan(1))
  })

  it('asks the account list of the company the page is opened for', async () => {
    const fetchMock = counterpartiesSpy([PARTY_ROW])
    const { port } = recordingPort(() => found(ledgerBody()))
    renderFinance(<PartyLedgerWorkspacePage companyId={COMPANY_A} base="/cari/hesap-takibi" title={WORKSPACE_TITLE_CARI} />,
      { port, router: true })

    await screen.findByRole('option', { name: 'C-100 - SENTETIK Alici' })
    const init = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect((init[1].headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY_A)
  })
})

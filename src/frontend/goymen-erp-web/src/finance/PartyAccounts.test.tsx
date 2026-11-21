/**
 * B-05 / M-03 (Muhasebe > Cari Hesaplar): the open-account list and one account's open items — the server's sums and
 * items as they came, the stated refusals, the withheld amounts and the distinct failure states. Component evidence
 * only (SENTETIK).
 */

import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { OpenPartyAccounts, PartyAccountOpenItems } from './PartyAccounts'
import { PARTY_ACCOUNT_TOO_MANY_CODE, PARTY_ACCOUNTS_TOO_MANY_CODE } from './partyAccountsHttp'
import {
  ACCOUNTS_EMPTY,
  ACCOUNTS_FORBIDDEN,
  ACCOUNTS_TOO_MANY,
  ITEMS_EMPTY,
  ITEMS_REDACTED,
  ITEMS_TOO_MANY_FACTS,
  ITEMS_TOO_MANY_OPEN,
} from './partyAccountsText'
import { LEDGER_ACCOUNT, LEDGER_PARTY, LEDGER_TRY } from './partyLedgerFixtures'
import { COMPANY_A, renderFinance } from './testHarness'

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))

const ITEM_SOON = 'c9000000-0000-4000-8000-000000000009'
const ITEM_LATE = 'cc000000-0000-4000-8000-00000000000c'
const SOURCE = 'ca000000-0000-4000-8000-00000000000a'
const KEY = `${LEDGER_PARTY}:${LEDGER_ACCOUNT}`

const money = (amount: string) => ({ amount, currencyId: LEDGER_TRY })
const visible = (amount: string) => ({ disclosure: 'Visible', money: money(amount) })
const redacted = { disclosure: 'Redacted', money: null }

function reply(body: unknown, status = 200) {
  const fetchMock = vi.fn(async (..._args: unknown[]) => new Response(JSON.stringify(body), {
    status, headers: { 'Content-Type': 'application/json' },
  }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const LIST = {
  companyId: COMPANY_A,
  accounts: [{
    partyId: LEDGER_PARTY,
    companyAccountId: LEDGER_ACCOUNT,
    currencies: [{
      currencyId: LEDGER_TRY, receivableCount: 2, openReceivable: money('1000.10'), payableCount: 1,
      openPayable: money('400.00'),
    }],
  }],
}

function account(withheld = false, openItems: unknown[] = []) {
  const amount = withheld ? redacted : visible('1000.10')
  return {
    companyId: COMPANY_A,
    partyId: LEDGER_PARTY,
    companyAccountId: LEDGER_ACCOUNT,
    currencies: [{
      currencyId: LEDGER_TRY,
      debitTotal: amount,
      creditTotal: withheld ? redacted : visible('0'),
      balance: withheld ? { side: null, amount: redacted } : { side: 'Debit', amount: visible('1000.10') },
      openReceivable: amount,
      openPayable: withheld ? redacted : visible('0'),
    }],
    openItemsDisclosure: withheld ? 'Redacted' : 'Visible',
    openItems,
  }
}

function item(openItemId: string, direction: string, dueOn: string | null, outstanding: string) {
  return {
    openItemId, direction, sourceId: SOURCE, currencyId: LEDGER_TRY, dueOn,
    originalAmount: money('1000.10'), outstandingAmount: money(outstanding),
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
  boundary.report.mockClear()
})

describe('open party accounts (M-03)', () => {
  it('shows the server sums per account and currency, names a known account and links to its ledger', async () => {
    const fetchMock = reply(LIST)
    renderFinance(<OpenPartyAccounts companyId={COMPANY_A} base="/muhasebe/cari-hesaplar"
      labels={new Map([[KEY, 'C-100 - SENTETIK Alici']])} />, { port: null, router: true })

    const row = await screen.findByRole('row', { name: /C-100 - SENTETIK Alici/ })
    expect(row.getAttribute('data-party-account')).toBe(KEY)
    expect(row.textContent).toContain('1.000,10')
    expect(row.textContent).toContain('400,00')
    const link = screen.getByRole('link', { name: /Ekstreyi aç: C-100/ })
    expect(link.getAttribute('href')).toContain(LEDGER_ACCOUNT)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toMatch(/\/finance\/party-accounts$/)
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY_A)
  })

  it('shows an unknown account by its reference, never a guessed name', async () => {
    reply(LIST)
    renderFinance(<OpenPartyAccounts companyId={COMPANY_A} base="/muhasebe/cari-hesaplar" labels={new Map()} />,
      { port: null, router: true })
    expect(await screen.findByText(new RegExp(`Cari ref\\. …${LEDGER_PARTY.slice(-8)}`))).toBeTruthy()
  })

  it('says when no account has an open item', async () => {
    reply({ companyId: COMPANY_A, accounts: [] })
    renderFinance(<OpenPartyAccounts companyId={COMPANY_A} base="/x" labels={new Map()} />, { port: null, router: true })
    expect(await screen.findByText(ACCOUNTS_EMPTY)).toBeTruthy()
  })

  it('says the list is refused rather than cut when the company has too many open items', async () => {
    reply({ type: 'about:blank', status: 422, code: PARTY_ACCOUNTS_TOO_MANY_CODE }, 422)
    renderFinance(<OpenPartyAccounts companyId={COMPANY_A} base="/x" labels={new Map()} />, { port: null, router: true })
    expect((await screen.findByRole('alert')).textContent).toBe(ACCOUNTS_TOO_MANY)
  })

  it('says the amounts are not granted on 403 and reports it to the boundary', async () => {
    reply({ status: 403 }, 403)
    renderFinance(<OpenPartyAccounts companyId={COMPANY_A} base="/x" labels={new Map()} />, { port: null, router: true })
    expect(await screen.findByText(ACCOUNTS_FORBIDDEN)).toBeTruthy()
    expect(boundary.report).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['an unknown 422 code', { code: 'something_else' }, 422, /beklenmeyen|doğrulanamadı/i],
    ['a list of another company', { companyId: 'bbbbbbbb-0000-4000-8000-000000000002', accounts: [] }, 200,
      /beklenmeyen|doğrulanamadı/i],
    ['a service outage', { status: 503 }, 503, /Servis geçici olarak kullanılamıyor/],
  ])('shows a distinct state for %s', async (_name, body, status, text) => {
    reply(body, status)
    renderFinance(<OpenPartyAccounts companyId={COMPANY_A} base="/x" labels={new Map()} />, { port: null, router: true })
    expect((await screen.findAllByText(text)).length).toBeGreaterThan(0)
    expect(screen.queryByRole('table')).toBeNull()
  })
})

describe('party account open items (M-03)', () => {
  it('lists the open items in the server order with their amounts', async () => {
    const fetchMock = reply(account(false, [
      item(ITEM_SOON, 'Payable', '2026-10-01', '400.00'),
      item(ITEM_LATE, 'Receivable', null, '1000.10'),
    ]))
    renderFinance(<PartyAccountOpenItems companyId={COMPANY_A} partyId={LEDGER_PARTY} companyAccountId={LEDGER_ACCOUNT} />,
      { port: null })

    await screen.findByRole('table')
    const rows = Array.from(document.querySelectorAll('[data-open-item]'))
    expect(rows.map((row) => row.getAttribute('data-open-item'))).toEqual([ITEM_SOON, ITEM_LATE])
    expect(rows[0].textContent).toContain('01.10.2026')
    expect(rows[0].textContent).toContain('Borç (alış)')
    expect(rows[1].textContent).toContain('Vadesiz')
    expect(rows[1].textContent).toContain('1.000,10')
    const [url] = fetchMock.mock.calls[0] as [string]
    expect(url).toMatch(new RegExp(`/finance/party-accounts/${LEDGER_PARTY}/${LEDGER_ACCOUNT}$`))
  })

  it('says the account has no open item', async () => {
    reply(account(false, []))
    renderFinance(<PartyAccountOpenItems companyId={COMPANY_A} partyId={LEDGER_PARTY} companyAccountId={LEDGER_ACCOUNT} />,
      { port: null })
    expect(await screen.findByText(ITEMS_EMPTY)).toBeTruthy()
  })

  it('shows no amount when the items are redacted', async () => {
    reply(account(true, []))
    renderFinance(<PartyAccountOpenItems companyId={COMPANY_A} partyId={LEDGER_PARTY} companyAccountId={LEDGER_ACCOUNT} />,
      { port: null })
    expect(await screen.findByText(ITEMS_REDACTED)).toBeTruthy()
    expect(document.body.textContent).not.toContain('1.000,10')
  })

  it.each([
    [PARTY_ACCOUNT_TOO_MANY_CODE, ITEMS_TOO_MANY_OPEN],
    ['finance_party_balance_too_many_facts', ITEMS_TOO_MANY_FACTS],
  ])('says the view is refused for %s', async (code, text) => {
    reply({ status: 422, code }, 422)
    renderFinance(<PartyAccountOpenItems companyId={COMPANY_A} partyId={LEDGER_PARTY} companyAccountId={LEDGER_ACCOUNT} />,
      { port: null })
    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(text))
  })

  it('reads nothing for an account that is not a bound id', async () => {
    const fetchMock = reply(account())
    renderFinance(<PartyAccountOpenItems companyId={COMPANY_A} partyId="not-a-uuid" companyAccountId={LEDGER_ACCOUNT} />,
      { port: null })
    expect((await screen.findAllByText(/beklenmeyen|doğrulanamadı/i)).length).toBeGreaterThan(0)
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

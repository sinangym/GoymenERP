/**
 * B-05 / M-03: the strict party-account decoders — exact keys, the asked company and account, unique keys, a side
 * without items sums to zero, withheld items are an empty list, and no mixed disclosure. SENTETIK ids only.
 */

import { describe, expect, it } from 'vitest'
import {
  decodePartyAccount,
  decodePartyAccountAnswer,
  decodePartyAccountList,
  decodePartyAccountListAnswer,
} from './partyAccountsContract'
import { LEDGER_ACCOUNT, LEDGER_PARTY, LEDGER_TRY, LEDGER_USD } from './partyLedgerFixtures'
import { COMPANY_A, COMPANY_B } from './testHarness'

const ITEM = 'c9000000-0000-4000-8000-000000000009'
const SOURCE = 'ca000000-0000-4000-8000-00000000000a'
const OTHER_ACCOUNT = 'cb000000-0000-4000-8000-00000000000b'

const money = (amount: string, currencyId = LEDGER_TRY) => ({ amount, currencyId })
const visible = (amount: string, currencyId = LEDGER_TRY) => ({ disclosure: 'Visible', money: money(amount, currencyId) })
const redacted = { disclosure: 'Redacted', money: null }

function openCurrency(patch: Record<string, unknown> = {}) {
  return {
    currencyId: LEDGER_TRY,
    receivableCount: 2,
    openReceivable: money('800.05'),
    payableCount: 0,
    openPayable: money('0'),
    ...patch,
  }
}

function list(accounts: unknown[] = [{ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [openCurrency()] }],
  companyId = COMPANY_A) {
  return { companyId, accounts }
}

function balanceCurrency(withheld = false) {
  const amount = withheld ? redacted : visible('1000.00')
  return {
    currencyId: LEDGER_TRY,
    debitTotal: amount,
    creditTotal: withheld ? redacted : visible('400.00'),
    balance: withheld ? { side: null, amount: redacted } : { side: 'Debit', amount: visible('600.00') },
    openReceivable: amount,
    openPayable: withheld ? redacted : visible('400.00'),
  }
}

function item(patch: Record<string, unknown> = {}) {
  return {
    openItemId: ITEM,
    direction: 'Receivable',
    sourceId: SOURCE,
    currencyId: LEDGER_TRY,
    dueOn: '2026-10-10',
    originalAmount: money('1000.00'),
    outstandingAmount: money('600.00'),
    ...patch,
  }
}

function view(patch: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY_A,
    partyId: LEDGER_PARTY,
    companyAccountId: LEDGER_ACCOUNT,
    currencies: [balanceCurrency()],
    openItemsDisclosure: 'Visible',
    openItems: [item()],
    ...patch,
  }
}

describe('party account list decoder (M-03)', () => {
  it('accepts the server list and an empty one', () => {
    expect(decodePartyAccountList(list(), COMPANY_A)).toEqual({ ok: true, value: list() })
    expect(decodePartyAccountList(list([]), COMPANY_A)).toEqual({ ok: true, value: list([]) })
    const twoCurrencies = list([{
      partyId: LEDGER_PARTY,
      companyAccountId: LEDGER_ACCOUNT,
      currencies: [openCurrency(), openCurrency({
        currencyId: LEDGER_USD, receivableCount: 0, openReceivable: money('0', LEDGER_USD), payableCount: 1,
        openPayable: money('75.25', LEDGER_USD),
      })],
    }])
    expect(decodePartyAccountList(twoCurrencies, COMPANY_A).ok).toBe(true)
  })

  it.each([
    ['another company', list(undefined, COMPANY_B)],
    ['an extra field', { ...list(), total: '800.05' }],
    ['a duplicated account', list([
      { partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [openCurrency()] },
      { partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [openCurrency()] },
    ])],
    ['an account without currencies', list([{ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [] }])],
    ['a duplicated currency', list([{
      partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [openCurrency(), openCurrency()],
    }])],
    ['a currency without items', list([{
      partyId: LEDGER_PARTY, companyAccountId: OTHER_ACCOUNT,
      currencies: [openCurrency({ receivableCount: 0, openReceivable: money('0') })],
    }])],
    ['an amount on a side without items', list([{
      partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [openCurrency({ openPayable: money('5') })],
    }])],
    ['a money in another currency', list([{
      partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT,
      currencies: [openCurrency({ openReceivable: money('800.05', LEDGER_USD) })],
    }])],
    ['a localized amount', list([{
      partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [openCurrency({ openReceivable: money('800,05') })],
    }])],
    ['a fractional count', list([{
      partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies: [openCurrency({ receivableCount: 1.5 })],
    }])],
  ])('refuses %s', (_name, value) => {
    expect(decodePartyAccountList(value, COMPANY_A).ok).toBe(false)
  })

  it('reads the stated refusal marker and nothing else as a refusal', () => {
    expect(decodePartyAccountListAnswer({ partyAccountsRefusal: 'TooManyOpenItems' }, COMPANY_A))
      .toEqual({ ok: true, value: { kind: 'refused', refusal: 'TooManyOpenItems' } })
    expect(decodePartyAccountListAnswer({ partyAccountsRefusal: 'Other' }, COMPANY_A).ok).toBe(false)
    expect(decodePartyAccountListAnswer(list(), COMPANY_A)).toEqual({ ok: true, value: { kind: 'list', list: list() } })
  })
})

describe('party account view decoder (M-03)', () => {
  it('accepts a visible view, an empty one and a withheld one', () => {
    expect(decodePartyAccount(view(), COMPANY_A, LEDGER_PARTY, LEDGER_ACCOUNT)).toEqual({ ok: true, value: view() })
    expect(decodePartyAccount(view({ openItems: [], currencies: [] }), COMPANY_A, LEDGER_PARTY, LEDGER_ACCOUNT).ok)
      .toBe(true)
    expect(decodePartyAccount(view({ openItems: [item({ dueOn: null, direction: 'Payable' })] }), COMPANY_A,
      LEDGER_PARTY, LEDGER_ACCOUNT).ok).toBe(true)
    const withheld = view({ currencies: [balanceCurrency(true)], openItemsDisclosure: 'Redacted', openItems: [] })
    expect(decodePartyAccount(withheld, COMPANY_A, LEDGER_PARTY, LEDGER_ACCOUNT).ok).toBe(true)
  })

  it.each([
    ['another company', view({ companyId: COMPANY_B })],
    ['another account than asked', view({ companyAccountId: OTHER_ACCOUNT })],
    ['an extra field', { ...view(), balance: '600.00' }],
    ['withheld items that are still listed', view({ currencies: [], openItemsDisclosure: 'Redacted' })],
    ['a summary disclosed differently from the items', view({ openItemsDisclosure: 'Redacted', openItems: [] })],
    ['an unknown disclosure', view({ currencies: [], openItemsDisclosure: 'Partial', openItems: [] })],
    ['an unknown direction', view({ openItems: [item({ direction: 'Both' })] })],
    ['a bad due date', view({ openItems: [item({ dueOn: '10.10.2026' })] })],
    ['an item money in another currency', view({ openItems: [item({ outstandingAmount: money('600.00', LEDGER_USD) })] })],
    ['a duplicated item', view({ openItems: [item(), item()] })],
    ['an item with an extra field', view({ openItems: [{ ...item(), status: 'Open' }] })],
  ])('refuses %s', (_name, value) => {
    expect(decodePartyAccount(value, COMPANY_A, LEDGER_PARTY, LEDGER_ACCOUNT).ok).toBe(false)
  })

  it('reads both stated refusals as markers', () => {
    for (const refusal of ['TooManyOpenItems', 'TooManyFacts'] as const) {
      expect(decodePartyAccountAnswer({ partyAccountRefusal: refusal }, COMPANY_A, LEDGER_PARTY, LEDGER_ACCOUNT))
        .toEqual({ ok: true, value: { kind: 'refused', refusal } })
    }
    expect(decodePartyAccountAnswer({ partyAccountRefusal: 'Invalid' }, COMPANY_A, LEDGER_PARTY, LEDGER_ACCOUNT).ok)
      .toBe(false)
  })
})

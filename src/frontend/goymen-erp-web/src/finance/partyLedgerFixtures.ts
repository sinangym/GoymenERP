/**
 * SENTETIK party-ledger bodies in the server's shape (PartyLedgerV1.cs), for the decoder and screen tests only.
 * The balances are the server's; these fixtures state them, they do not compute them.
 */

import { COMPANY_A } from './testHarness'

export const LEDGER_PARTY = 'c5000000-0000-4000-8000-000000000005'
export const LEDGER_ACCOUNT = 'c6000000-0000-4000-8000-000000000006'
export const LEDGER_TRY = 'c7000000-0000-4000-8000-000000000007'
export const LEDGER_USD = 'c8000000-0000-4000-8000-000000000008'
export const IDS = {
  openItem: 'e1000000-0000-4000-8000-0000000000a1',
  document: 'e1000000-0000-4000-8000-0000000000d1',
  receipt: 'e2000000-0000-4000-8000-0000000000a2',
  reversal: 'e3000000-0000-4000-8000-0000000000a3',
  payment: 'e4000000-0000-4000-8000-0000000000a4',
  collection: 'e5000000-0000-4000-8000-0000000000a5',
  instrument: 'e5000000-0000-4000-8000-0000000000c5',
  usdReceipt: 'e6000000-0000-4000-8000-0000000000a6',
} as const

type Amount = string | null

function amount(value: Amount, currencyId: string) {
  return value === null
    ? { disclosure: 'Redacted', money: null }
    : { disclosure: 'Visible', money: { amount: value, currencyId } }
}

function balance(side: string, value: Amount, currencyId: string) {
  return { side: value === null ? null : side, amount: amount(value, currencyId) }
}

let entrySeed = 0

function entry(
  kind: string,
  occurredOn: string,
  side: 'Debit' | 'Credit',
  value: Amount,
  balanceSide: string,
  balanceValue: Amount,
  sourceId: string,
  currencyId: string,
  links: { reversesSourceId?: string; instrumentId?: string; documentId?: string; dueOn?: string } = {},
) {
  entrySeed += 1
  return {
    kind,
    entryId: `f${String(entrySeed).padStart(7, '0')}-0000-4000-8000-000000000000`,
    occurredOn,
    recordedAtUtc: `${occurredOn}T09:00:00+00:00`,
    side,
    amount: amount(value, currencyId),
    runningBalance: balance(balanceSide, balanceValue, currencyId),
    sourceId,
    reversesSourceId: links.reversesSourceId ?? null,
    instrumentId: links.instrumentId ?? null,
    documentId: links.documentId ?? null,
    dueOn: links.dueOn ?? null,
  }
}

/** The five-row story of the backend tests (and one USD row), visible or with every amount withheld. */
export function ledgerBody(options: { readonly redacted?: boolean; readonly withUsd?: boolean } = {}) {
  const v = (value: string): Amount => (options.redacted === true ? null : value)
  const tl = {
    currencyId: LEDGER_TRY,
    entries: [
      entry('OpenItem', '2026-09-10', 'Debit', v('1000.00'), 'Debit', v('1000.00'), IDS.openItem, LEDGER_TRY,
        { documentId: IDS.document, dueOn: '2026-10-10' }),
      entry('ManualReceipt', '2026-09-15', 'Credit', v('400.00'), 'Debit', v('600.00'), IDS.receipt, LEDGER_TRY),
      entry('ManualReceiptReversal', '2026-09-20', 'Debit', v('400.00'), 'Debit', v('1000.00'), IDS.reversal, LEDGER_TRY,
        { reversesSourceId: IDS.receipt }),
      entry('ManualBankPayment', '2026-09-22', 'Debit', v('50.00'), 'Debit', v('1050.00'), IDS.payment, LEDGER_TRY),
      entry('CustomerInstrumentCollection', '2026-09-25', 'Credit', v('1200.00'), 'Credit', v('150.00'), IDS.collection,
        LEDGER_TRY, { instrumentId: IDS.instrument }),
    ],
    debitTotal: amount(v('1450.00'), LEDGER_TRY),
    creditTotal: amount(v('1600.00'), LEDGER_TRY),
    closingBalance: balance('Credit', v('150.00'), LEDGER_TRY),
  }
  const usd = {
    currencyId: LEDGER_USD,
    entries: [entry('ManualReceipt', '2026-09-12', 'Credit', v('75.50'), 'Credit', v('75.50'), IDS.usdReceipt, LEDGER_USD)],
    debitTotal: amount(v('0.00'), LEDGER_USD),
    creditTotal: amount(v('75.50'), LEDGER_USD),
    closingBalance: balance('Credit', v('75.50'), LEDGER_USD),
  }
  return {
    companyId: COMPANY_A,
    partyId: LEDGER_PARTY,
    companyAccountId: LEDGER_ACCOUNT,
    from: '2026-09-01',
    to: '2026-09-30',
    carryForward: 'NoneOpen018',
    currencies: options.withUsd === false ? [tl] : [tl, usd],
  }
}

/**
 * The first-release party ledger (cari hesap ekstresi) — `GET /finance/party-ledger` (PartyLedgerV1.cs), decoded
 * strictly: exact keys, the context company, one currency per block, every amount as the server disclosed it.
 *
 * What the ledger is: one party account's RECORDED Finance facts in an inclusive date range — admitted open items,
 * recorded receipts, their reversals (their own rows), cheque / note collections and recorded payments — per currency,
 * with a running balance that starts at ZERO on the first day (OPEN-018: no carry-forward / devir). Currencies are
 * never converted or added (OPEN-012). The client computes nothing: sides, balances and totals are the server's.
 *
 * Two refusals are not "not found" and not a transport failure, so the HTTP adapter hands them to this decoder as
 * markers instead of a ledger: `Invalid` (400 — a malformed range) and `TooManyEntries` (422
 * `finance_party_ledger_too_many_entries` — narrow the range; a cut ledger would show a wrong balance).
 */

import { isBoundUuid, isInstant, type AmountView, type Decoded } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText, isPositiveDecimalText } from './decimalText'

export const PARTY_LEDGER_ENTRY_KINDS = [
  'OpenItem',
  'ManualReceipt',
  'ManualReceiptReversal',
  'CustomerInstrumentCollection',
  'ManualBankPayment',
] as const
export type PartyLedgerEntryKind = (typeof PARTY_LEDGER_ENTRY_KINDS)[number]
export type PartyLedgerSide = 'Debit' | 'Credit'
export type PartyLedgerBalanceSide = 'Zero' | 'Debit' | 'Credit'

/** A balance: its side only with a visible amount (the side is derived from the amounts). */
export type PartyLedgerBalance =
  | { readonly side: PartyLedgerBalanceSide; readonly amount: AmountView & { readonly disclosure: 'Visible' } }
  | { readonly side: null; readonly amount: AmountView & { readonly disclosure: 'Redacted' | 'Unavailable' } }

export interface PartyLedgerEntry {
  readonly kind: PartyLedgerEntryKind
  readonly entryId: string
  readonly occurredOn: string
  readonly recordedAtUtc: string
  readonly side: PartyLedgerSide
  readonly amount: AmountView
  readonly runningBalance: PartyLedgerBalance
  readonly sourceId: string
  readonly reversesSourceId: string | null
  readonly instrumentId: string | null
  readonly documentId: string | null
  readonly dueOn: string | null
}

export interface PartyLedgerCurrency {
  readonly currencyId: string
  readonly entries: readonly PartyLedgerEntry[]
  readonly debitTotal: AmountView
  readonly creditTotal: AmountView
  readonly closingBalance: PartyLedgerBalance
}

export interface PartyLedgerView {
  readonly companyId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly from: string
  readonly to: string
  readonly carryForward: 'NoneOpen018'
  readonly currencies: readonly PartyLedgerCurrency[]
}

export type PartyLedgerAnswer =
  | { readonly kind: 'ledger'; readonly ledger: PartyLedgerView }
  | { readonly kind: 'refused'; readonly refusal: PartyLedgerRefusal }

export type PartyLedgerRefusal = 'Invalid' | 'TooManyEntries'

/** The adapter's marker for a refusal the server states by status (400 / 422), never a ledger. */
export interface PartyLedgerRefusalMarker {
  readonly partyLedgerRefusal: PartyLedgerRefusal
}

export interface PartyLedgerRequest {
  readonly partyId: string
  readonly companyAccountId: string
  readonly from: string
  readonly to: string
}

const FAIL = { ok: false } as const
function ok<T>(value: T): Decoded<T> {
  return { ok: true, value }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value)
  return actual.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

function isDate(value: unknown): value is string {
  return typeof value === 'string' && isDateOnly(value)
}

function isNullableUuid(value: unknown): value is string | null {
  return value === null || isBoundUuid(value)
}

/** A row's amount: positive when visible (a recorded fact is never zero). */
function decodeAmount(value: unknown, currencyId: string, allowZero: boolean): AmountView | null {
  if (!isRecord(value) || !hasExactKeys(value, ['disclosure', 'money'])) return null
  if (value.disclosure === 'Visible') {
    const money = value.money
    if (!isRecord(money) || !hasExactKeys(money, ['amount', 'currencyId']) || money.currencyId !== currencyId) return null
    const valid = allowZero ? isNonNegativeDecimalText(money.amount) : isPositiveDecimalText(money.amount)
    return valid ? { disclosure: 'Visible', money: { amount: money.amount as string, currencyId } } : null
  }
  if ((value.disclosure === 'Redacted' || value.disclosure === 'Unavailable') && value.money === null) {
    return { disclosure: value.disclosure, money: null }
  }
  return null
}

function decodeBalance(value: unknown, currencyId: string): PartyLedgerBalance | null {
  if (!isRecord(value) || !hasExactKeys(value, ['side', 'amount'])) return null
  const amount = decodeAmount(value.amount, currencyId, true)
  if (amount === null) return null
  if (amount.disclosure !== 'Visible') {
    // A withheld amount withholds its side too.
    return value.side === null ? { side: null, amount } : null
  }
  const zero = !/[1-9]/.test(amount.money.amount)
  if (value.side === 'Zero' && zero) return { side: 'Zero', amount }
  if ((value.side === 'Debit' || value.side === 'Credit') && !zero) return { side: value.side, amount }
  return null
}

const ENTRY_KEYS = [
  'kind', 'entryId', 'occurredOn', 'recordedAtUtc', 'side', 'amount', 'runningBalance', 'sourceId', 'reversesSourceId',
  'instrumentId', 'documentId', 'dueOn',
] as const

/** The column each kind is written in — the server's rule, checked so a crossed row cannot be shown. */
const SIDE_OF: Readonly<Record<PartyLedgerEntryKind, PartyLedgerSide>> = {
  OpenItem: 'Debit',
  ManualReceipt: 'Credit',
  ManualReceiptReversal: 'Debit',
  CustomerInstrumentCollection: 'Credit',
  ManualBankPayment: 'Debit',
}

function decodeEntry(value: unknown, currencyId: string, from: string, to: string): PartyLedgerEntry | null {
  if (!isRecord(value) || !hasExactKeys(value, ENTRY_KEYS)) return null
  const kind = PARTY_LEDGER_ENTRY_KINDS.find((candidate) => candidate === value.kind)
  if (kind === undefined || value.side !== SIDE_OF[kind]) return null
  if (!isBoundUuid(value.entryId) || !isDate(value.occurredOn) || value.occurredOn < from || value.occurredOn > to
    || !isInstant(value.recordedAtUtc) || !isBoundUuid(value.sourceId) || !isNullableUuid(value.reversesSourceId)
    || !isNullableUuid(value.instrumentId) || !isNullableUuid(value.documentId)
    || !(value.dueOn === null || isDate(value.dueOn))) {
    return null
  }
  // Each link belongs to its kind only.
  if ((value.reversesSourceId === null) !== (kind !== 'ManualReceiptReversal')) return null
  if (value.instrumentId !== null && kind !== 'CustomerInstrumentCollection') return null
  if (kind !== 'OpenItem' && (value.documentId !== null || value.dueOn !== null)) return null
  if (kind === 'OpenItem' && value.documentId === null) return null
  const amount = decodeAmount(value.amount, currencyId, false)
  const runningBalance = decodeBalance(value.runningBalance, currencyId)
  if (amount === null || runningBalance === null || amount.disclosure !== runningBalance.amount.disclosure) return null
  return {
    kind,
    entryId: value.entryId,
    occurredOn: value.occurredOn,
    recordedAtUtc: value.recordedAtUtc,
    side: SIDE_OF[kind],
    amount,
    runningBalance,
    sourceId: value.sourceId,
    reversesSourceId: value.reversesSourceId,
    instrumentId: value.instrumentId,
    documentId: value.documentId,
    dueOn: value.dueOn,
  }
}

function decodeCurrency(value: unknown, from: string, to: string): PartyLedgerCurrency | null {
  if (!isRecord(value) || !hasExactKeys(value, ['currencyId', 'entries', 'debitTotal', 'creditTotal', 'closingBalance'])) {
    return null
  }
  if (!isBoundUuid(value.currencyId) || !Array.isArray(value.entries) || value.entries.length === 0) return null
  const currencyId = value.currencyId
  const entries: PartyLedgerEntry[] = []
  for (const raw of value.entries) {
    const entry = decodeEntry(raw, currencyId, from, to)
    if (entry === null) return null
    // The server's order is by date first; a row out of date order is not a ledger.
    if (entries.length > 0 && entry.occurredOn < entries[entries.length - 1].occurredOn) return null
    entries.push(entry)
  }
  const debitTotal = decodeAmount(value.debitTotal, currencyId, true)
  const creditTotal = decodeAmount(value.creditTotal, currencyId, true)
  const closingBalance = decodeBalance(value.closingBalance, currencyId)
  if (debitTotal === null || creditTotal === null || closingBalance === null) return null
  // The closing balance is the last row's running balance, as the server computed it.
  const last = entries[entries.length - 1].runningBalance
  if (last.side !== closingBalance.side || last.amount.money?.amount !== closingBalance.amount.money?.amount) return null
  return { currencyId, entries, debitTotal, creditTotal, closingBalance }
}

export function decodePartyLedgerAnswer(value: unknown, companyId: string): Decoded<PartyLedgerAnswer> {
  if (isRecord(value) && hasExactKeys(value, ['partyLedgerRefusal'])) {
    return value.partyLedgerRefusal === 'Invalid' || value.partyLedgerRefusal === 'TooManyEntries'
      ? ok({ kind: 'refused', refusal: value.partyLedgerRefusal })
      : FAIL
  }
  const keys = ['companyId', 'partyId', 'companyAccountId', 'from', 'to', 'carryForward', 'currencies']
  if (!isRecord(value) || !hasExactKeys(value, keys)) return FAIL
  if (value.companyId !== companyId || !isBoundUuid(value.partyId) || !isBoundUuid(value.companyAccountId)
    || !isDate(value.from) || !isDate(value.to) || value.to < value.from || value.carryForward !== 'NoneOpen018'
    || !Array.isArray(value.currencies)) {
    return FAIL
  }
  const currencies: PartyLedgerCurrency[] = []
  const seen = new Set<string>()
  for (const raw of value.currencies) {
    const currency = decodeCurrency(raw, value.from, value.to)
    if (currency === null || seen.has(currency.currencyId)) return FAIL
    seen.add(currency.currencyId)
    currencies.push(currency)
  }
  return ok({
    kind: 'ledger',
    ledger: {
      companyId,
      partyId: value.partyId,
      companyAccountId: value.companyAccountId,
      from: value.from,
      to: value.to,
      carryForward: 'NoneOpen018',
      currencies,
    },
  })
}

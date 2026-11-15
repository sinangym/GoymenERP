/**
 * B-05 / M-03 (FNS-D14, KLN-D3): the party-account reads behind Muhasebe > Cari Hesaplar.
 *
 * - `GET /finance/party-accounts` — every party account of the context company that has a still-open item, with the
 *   count and the exact sum of its open receivable and open payable items per currency. It is "open receivable / open
 *   payable", never a balance (a balance needs every recorded fact: the API-04 summary).
 * - `GET /finance/party-accounts/{partyId}/{companyAccountId}` — one account: the API-04 summary as it is and the
 *   account's open items by due date. Without the amount authority the items are withheld and the disclosure says why.
 *
 * Decoders are strict: exact keys, the requested company, unique keys, no mixed disclosure; anything else is a FAIL
 * and the screen says the answer is unexpected. Nothing is converted or added across currencies (OPEN-012).
 */

import { isBoundUuid, type AmountDisclosure, type Decoded, type MoneyText } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText } from './decimalText'
import { decodePartyBalance, type PartyBalanceCurrency } from './partyLedgerContract'

export type PartyAccountDirection = 'Receivable' | 'Payable'

export interface PartyAccountOpenCurrency {
  readonly currencyId: string
  readonly receivableCount: number
  readonly openReceivable: MoneyText
  readonly payableCount: number
  readonly openPayable: MoneyText
}

export interface PartyAccountSummary {
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencies: readonly PartyAccountOpenCurrency[]
}

export interface PartyAccountList {
  readonly companyId: string
  readonly accounts: readonly PartyAccountSummary[]
}

export interface PartyAccountOpenItem {
  readonly openItemId: string
  readonly direction: PartyAccountDirection
  readonly sourceId: string
  readonly currencyId: string
  readonly dueOn: string | null
  readonly originalAmount: MoneyText
  readonly outstandingAmount: MoneyText
}

export interface PartyAccountView {
  readonly companyId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencies: readonly PartyBalanceCurrency[]
  readonly openItemsDisclosure: AmountDisclosure
  readonly openItems: readonly PartyAccountOpenItem[]
}

/** The 422 refusals the server states by code, handed to the decoders as markers by the transport. */
export type PartyAccountsRefusal = 'TooManyOpenItems'
export type PartyAccountRefusal = 'TooManyOpenItems' | 'TooManyFacts'

export type PartyAccountListAnswer =
  | { readonly kind: 'list'; readonly list: PartyAccountList }
  | { readonly kind: 'refused'; readonly refusal: PartyAccountsRefusal }

export type PartyAccountAnswer =
  | { readonly kind: 'account'; readonly account: PartyAccountView }
  | { readonly kind: 'refused'; readonly refusal: PartyAccountRefusal }

const FAIL = { ok: false } as const

function ok<T>(value: T): Decoded<T> {
  return { ok: true, value }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

function isCount(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

function isZeroText(amount: string): boolean {
  return !/[1-9]/.test(amount)
}

function decodeMoney(value: unknown, currencyId: string): MoneyText | null {
  if (!isRecord(value) || !hasExactKeys(value, ['amount', 'currencyId']) || value.currencyId !== currencyId
    || !isNonNegativeDecimalText(value.amount)) {
    return null
  }
  return { amount: value.amount, currencyId }
}

const OPEN_CURRENCY_KEYS = ['currencyId', 'receivableCount', 'openReceivable', 'payableCount', 'openPayable']

function decodeOpenCurrency(value: unknown): PartyAccountOpenCurrency | null {
  if (!isRecord(value) || !hasExactKeys(value, OPEN_CURRENCY_KEYS) || !isBoundUuid(value.currencyId)
    || !isCount(value.receivableCount) || !isCount(value.payableCount)) {
    return null
  }
  const currencyId = value.currencyId
  const openReceivable = decodeMoney(value.openReceivable, currencyId)
  const openPayable = decodeMoney(value.openPayable, currencyId)
  if (openReceivable === null || openPayable === null) return null
  // A currency is listed only for an open item; a side without items sums to zero.
  if (value.receivableCount + value.payableCount === 0) return null
  if (value.receivableCount === 0 && !isZeroText(openReceivable.amount)) return null
  if (value.payableCount === 0 && !isZeroText(openPayable.amount)) return null
  return {
    currencyId,
    receivableCount: value.receivableCount,
    openReceivable,
    payableCount: value.payableCount,
    openPayable,
  }
}

function decodeSummary(value: unknown): PartyAccountSummary | null {
  if (!isRecord(value) || !hasExactKeys(value, ['partyId', 'companyAccountId', 'currencies'])
    || !isBoundUuid(value.partyId) || !isBoundUuid(value.companyAccountId) || !Array.isArray(value.currencies)
    || value.currencies.length === 0) {
    return null
  }
  const currencies: PartyAccountOpenCurrency[] = []
  const seen = new Set<string>()
  for (const raw of value.currencies) {
    const currency = decodeOpenCurrency(raw)
    if (currency === null || seen.has(currency.currencyId)) return null
    seen.add(currency.currencyId)
    currencies.push(currency)
  }
  return { partyId: value.partyId, companyAccountId: value.companyAccountId, currencies }
}

export function decodePartyAccountList(value: unknown, companyId: string): Decoded<PartyAccountList> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'accounts']) || value.companyId !== companyId
    || !Array.isArray(value.accounts)) {
    return FAIL
  }
  const accounts: PartyAccountSummary[] = []
  const seen = new Set<string>()
  for (const raw of value.accounts) {
    const account = decodeSummary(raw)
    if (account === null) return FAIL
    const key = `${account.partyId}:${account.companyAccountId}`
    if (seen.has(key)) return FAIL
    seen.add(key)
    accounts.push(account)
  }
  return ok({ companyId, accounts })
}

export function decodePartyAccountListAnswer(value: unknown, companyId: string): Decoded<PartyAccountListAnswer> {
  if (isRecord(value) && hasExactKeys(value, ['partyAccountsRefusal'])) {
    return value.partyAccountsRefusal === 'TooManyOpenItems' ? ok({ kind: 'refused', refusal: 'TooManyOpenItems' }) : FAIL
  }
  const list = decodePartyAccountList(value, companyId)
  return list.ok ? ok({ kind: 'list', list: list.value }) : FAIL
}

const OPEN_ITEM_KEYS = [
  'openItemId', 'direction', 'sourceId', 'currencyId', 'dueOn', 'originalAmount', 'outstandingAmount',
]

function decodeOpenItem(value: unknown): PartyAccountOpenItem | null {
  if (!isRecord(value) || !hasExactKeys(value, OPEN_ITEM_KEYS) || !isBoundUuid(value.openItemId)
    || !isBoundUuid(value.sourceId) || !isBoundUuid(value.currencyId)
    || (value.direction !== 'Receivable' && value.direction !== 'Payable')
    || !(value.dueOn === null || (typeof value.dueOn === 'string' && isDateOnly(value.dueOn)))) {
    return null
  }
  const currencyId = value.currencyId
  const originalAmount = decodeMoney(value.originalAmount, currencyId)
  const outstandingAmount = decodeMoney(value.outstandingAmount, currencyId)
  if (originalAmount === null || outstandingAmount === null) return null
  return {
    openItemId: value.openItemId,
    direction: value.direction,
    sourceId: value.sourceId,
    currencyId,
    dueOn: value.dueOn,
    originalAmount,
    outstandingAmount,
  }
}

const VIEW_KEYS = ['companyId', 'partyId', 'companyAccountId', 'currencies', 'openItemsDisclosure', 'openItems']

/** One account's view; it must be the account asked for, in the company asked for. */
export function decodePartyAccount(value: unknown, companyId: string, partyId: string,
  companyAccountId: string): Decoded<PartyAccountView> {
  if (!isRecord(value) || !hasExactKeys(value, VIEW_KEYS) || !Array.isArray(value.openItems)) return FAIL
  const summary = decodePartyBalance({
    companyId: value.companyId,
    partyId: value.partyId,
    companyAccountId: value.companyAccountId,
    currencies: value.currencies,
  }, companyId)
  if (!summary.ok || summary.value.partyId !== partyId || summary.value.companyAccountId !== companyAccountId) {
    return FAIL
  }
  const disclosure = value.openItemsDisclosure
  if (disclosure !== 'Visible' && disclosure !== 'Redacted' && disclosure !== 'Unavailable') return FAIL
  // The summary and the items are disclosed under one measurement; a mixed answer is not this contract's.
  if (summary.value.currencies.some((currency) => currency.debitTotal.disclosure !== disclosure)) return FAIL
  // Withheld items are an empty list, never a partial one.
  if (disclosure !== 'Visible' && value.openItems.length !== 0) return FAIL
  const openItems: PartyAccountOpenItem[] = []
  const seen = new Set<string>()
  for (const raw of value.openItems) {
    const item = decodeOpenItem(raw)
    if (item === null || seen.has(item.openItemId)) return FAIL
    seen.add(item.openItemId)
    openItems.push(item)
  }
  return ok({
    companyId,
    partyId,
    companyAccountId,
    currencies: summary.value.currencies,
    openItemsDisclosure: disclosure,
    openItems,
  })
}

export function decodePartyAccountAnswer(value: unknown, companyId: string, partyId: string,
  companyAccountId: string): Decoded<PartyAccountAnswer> {
  if (isRecord(value) && hasExactKeys(value, ['partyAccountRefusal'])) {
    return value.partyAccountRefusal === 'TooManyOpenItems' || value.partyAccountRefusal === 'TooManyFacts'
      ? ok({ kind: 'refused', refusal: value.partyAccountRefusal })
      : FAIL
  }
  const account = decodePartyAccount(value, companyId, partyId, companyAccountId)
  return account.ok ? ok({ kind: 'account', account: account.value }) : FAIL
}

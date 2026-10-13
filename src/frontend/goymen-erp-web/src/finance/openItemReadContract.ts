/**
 * FI04 open-item reads (#700): `GET /api/v1/finance/open-items?cursor` and `GET /api/v1/finance/open-items/{id}`.
 *
 * The decoder accepts exactly what `FinanceOpenItemViewV1` / `FinanceOpenItemPageV1` carry and refuses anything
 * else. Money and the status are the amount authority's: the server returns the status only together with both
 * visible amounts, so an answer that carries one without the other is refused rather than repaired. A hidden amount
 * is `money: null` and a hidden status is `null` — neither is ever turned into zero, "Closed" or any other value.
 */

import { isBoundUuid, isInstant, isPositiveInt64Text, type AmountView } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText, isPositiveDecimalText, positiveDifference, sameDecimal } from './decimalText'

export type OpenItemStatus = 'Open' | 'Closed'

export interface OpenItem {
  readonly id: string
  readonly companyId: string
  readonly sourceKind: string
  readonly sourceId: string
  readonly sourceLineId: string
  readonly sourceVersion: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly version: string
  readonly dueOn: string | null
  /** `null` whenever the caller may not read amounts: the status is derived from the remaining amount. */
  readonly status: OpenItemStatus | null
  readonly createdAtUtc: string
  readonly initialAmount: AmountView
  readonly outstandingAmount: AmountView
}

export interface OpenItemPage {
  readonly companyId: string
  readonly items: readonly OpenItem[]
  /** Opaque; `null` is the end. Sent back verbatim, never read. */
  readonly nextCursor: string | null
}

/** The server's page size and cursor bound (`FinanceOpenItemReadService.PageSize`, the endpoint's cursor limit). */
export const OPEN_ITEM_PAGE_SIZE = 50
export const OPEN_ITEM_CURSOR_MAX_LENGTH = 8192
const SOURCE_KIND_MAX_LENGTH = 128

const ITEM_KEYS = ['id', 'companyId', 'sourceKind', 'sourceId', 'sourceLineId', 'sourceVersion', 'partyId',
  'companyAccountId', 'currencyId', 'version', 'dueOn', 'status', 'createdAtUtc', 'initialAmount', 'outstandingAmount']
const PAGE_KEYS = ['companyId', 'items', 'nextCursor']

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : null
}

function hasExactly(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()

/** Stored text the server already holds trimmed: present, not padded, within the column's length. */
function canonicalText(value: unknown, maxLength: number): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= maxLength && value === value.trim()
}

function decodeAmount(value: unknown, currencyId: string, allowZero: boolean): AmountView | null {
  const body = record(value)
  if (body === null || !hasExactly(body, ['disclosure', 'money'])) return null
  if (body.disclosure === 'Redacted' || body.disclosure === 'Unavailable') {
    return body.money === null ? { disclosure: body.disclosure, money: null } : null
  }
  if (body.disclosure !== 'Visible') return null
  const money = record(body.money)
  if (money === null || !hasExactly(money, ['amount', 'currencyId'])) return null
  const amountOk = allowZero ? isNonNegativeDecimalText(money.amount) : isPositiveDecimalText(money.amount)
  if (!amountOk || typeof money.currencyId !== 'string' || !sameId(money.currencyId, currencyId)) return null
  return { disclosure: 'Visible', money: { amount: money.amount as string, currencyId } }
}

/** One open item of `companyId`, or `null` when the answer is not exactly the contract's. */
export function decodeOpenItem(value: unknown, companyId: string): OpenItem | null {
  const body = record(value)
  if (body === null || !hasExactly(body, ITEM_KEYS)) return null
  const { id, sourceKind, sourceId, sourceLineId, sourceVersion, partyId, companyAccountId, currencyId, version,
    dueOn, status, createdAtUtc } = body
  if (!isBoundUuid(id) || !isBoundUuid(body.companyId) || !sameId(body.companyId, companyId)
    || !isBoundUuid(sourceId) || !isBoundUuid(sourceLineId) || !isBoundUuid(partyId)
    || !isBoundUuid(companyAccountId) || !isBoundUuid(currencyId)
    || !canonicalText(sourceKind, SOURCE_KIND_MAX_LENGTH) || !canonicalText(sourceVersion, Number.MAX_SAFE_INTEGER)
    || !isPositiveInt64Text(version) || !isInstant(createdAtUtc)
    || !(dueOn === null || (typeof dueOn === 'string' && isDateOnly(dueOn)))
    || !(status === null || status === 'Open' || status === 'Closed')) return null
  const initialAmount = decodeAmount(body.initialAmount, currencyId, false)
  const outstandingAmount = decodeAmount(body.outstandingAmount, currencyId, true)
  if (initialAmount === null || outstandingAmount === null) return null
  // One authority decides all three disclosures together; a mixed answer is not this contract's.
  if (initialAmount.disclosure !== outstandingAmount.disclosure) return null
  if ((status !== null) !== (initialAmount.disclosure === 'Visible')) return null
  if (initialAmount.disclosure === 'Visible' && outstandingAmount.disclosure === 'Visible') {
    const initial = initialAmount.money.amount
    const outstanding = outstandingAmount.money.amount
    if (!sameDecimal(initial, outstanding) && positiveDifference(initial, outstanding) === null) return null
  }
  return { id, companyId: body.companyId, sourceKind, sourceId, sourceLineId, sourceVersion, partyId, companyAccountId,
    currencyId, version, dueOn, status, createdAtUtc, initialAmount, outstandingAmount }
}

/** The detail read's answer: the item asked for, of the company asked in. */
export function decodeOpenItemFor(value: unknown, companyId: string, openItemId: string): OpenItem | null {
  const item = decodeOpenItem(value, companyId)
  return item === null || !sameId(item.id, openItemId) ? null : item
}

export function decodeOpenItemPage(value: unknown, companyId: string): OpenItemPage | null {
  const body = record(value)
  if (body === null || !hasExactly(body, PAGE_KEYS)) return null
  if (!isBoundUuid(body.companyId) || !sameId(body.companyId, companyId)) return null
  if (!Array.isArray(body.items) || body.items.length > OPEN_ITEM_PAGE_SIZE) return null
  const { nextCursor } = body
  if (!(nextCursor === null || (typeof nextCursor === 'string' && nextCursor.trim().length > 0
    && nextCursor.length <= OPEN_ITEM_CURSOR_MAX_LENGTH))) return null
  const items: OpenItem[] = []
  const seen = new Set<string>()
  for (const candidate of body.items) {
    const item = decodeOpenItem(candidate, companyId)
    if (item === null || seen.has(item.id.toLowerCase())) return null
    // One read is answered under one amount authority: a page that discloses some rows and withholds others is refused.
    if (items.length > 0 && item.initialAmount.disclosure !== items[0].initialAmount.disclosure) return null
    seen.add(item.id.toLowerCase())
    items.push(item)
  }
  return { companyId: body.companyId, items, nextCursor }
}

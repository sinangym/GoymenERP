/**
 * API-03 / UI-15 (SAT-D11, delegation No 15): the order's line and document totals — the contract of
 * `GET /api/v1/sales/drafts/{id}/totals` (`GetSalesOrderDraftTotalsV1`, `SalesOrderDraftTotalsResponse`).
 *
 * Every amount is the SERVER's calculator's (`SalesOrderTotalsCalculator`); this file only reads what it answered and never
 * adds, multiplies or rounds. Rules taken from the backend contract (`SalesDraftTotalsV1.cs`, the Sales README "Order totals"),
 * not assumed:
 *  - every member is present, nullable ones as explicit null; unknown members or states, and numbers standing in for
 *    decimal text, are refused — a body that does not fit is never shown as totals;
 *  - the answer is bound to the detail on screen: the same version, commercial revision and currency, and every line of
 *    that detail exactly once with its own number, order unit and quantity;
 *  - every amount is plain decimal text with exactly `amountScale` decimals (the server pads to the profile's scale);
 *  - one shape per state: a `Computed` line has a net unit price and a net amount, an `Unpriced` / `PriceUnitNotOrderUnit` /
 *    `AmountOutOfRange` line has neither and its tax is `NotComputed`; the document is `Incomplete` (no net) when a line has
 *    no amount or there is no line at all, with the counts of the lines that have none; otherwise `Computed` with its net, or
 *    `AmountOutOfRange` (a sum beyond the decimal range: no net); VAT, withholding, gross and payable exist
 *    exactly when the tax is `Computed`, and `NoSource` (no VAT master) carries none of them — never zero.
 *
 * Decimal text stays text. A decoder returns null for anything it cannot vouch for; the caller fails closed.
 */

import { salesDraftDetailPath, type DraftDetail } from './contract'
import type { ViewOutcome } from '../shell/uiState'
import { isReferenceId } from './wirePrimitives'

/** API-03: the order's totals (GET, under the draft read AND the price read). */
export const salesDraftTotalsPath = (id: string) => `${salesDraftDetailPath(id)}/totals`

/** `SalesDraftTotalsStatesV1`. */
export const TOTALS_STATE = {
  computed: 'Computed',
  unpriced: 'Unpriced',
  priceUnitNotOrderUnit: 'PriceUnitNotOrderUnit',
  amountOutOfRange: 'AmountOutOfRange',
  incomplete: 'Incomplete',
  noSource: 'NoSource',
  notComputed: 'NotComputed',
} as const

export const LINE_TOTAL_STATES = [
  TOTALS_STATE.computed, TOTALS_STATE.unpriced, TOTALS_STATE.priceUnitNotOrderUnit, TOTALS_STATE.amountOutOfRange,
] as const
export type LineTotalState = (typeof LINE_TOTAL_STATES)[number]

export const DOCUMENT_TOTAL_STATES = [TOTALS_STATE.computed, TOTALS_STATE.incomplete, TOTALS_STATE.amountOutOfRange] as const
export type DocumentTotalState = (typeof DOCUMENT_TOTAL_STATES)[number]

export const TAX_STATES = [TOTALS_STATE.computed, TOTALS_STATE.noSource, TOTALS_STATE.notComputed] as const
export type TaxState = (typeof TAX_STATES)[number]

/** The tax amounts of a line or the document; all present exactly when the tax is `Computed`. */
export interface TaxAmounts {
  readonly vatAmount: string
  readonly withheldVatAmount: string
  readonly grossAmount: string
  readonly payableAmount: string
}

export interface LineTotal {
  readonly lineId: string
  readonly lineNo: number
  readonly orderUnitId: string
  readonly quantity: string
  readonly state: LineTotalState
  readonly netUnitPrice: string | null
  readonly netAmount: string | null
  readonly taxState: TaxState
  readonly vatPercent: string | null
  readonly withholdingShare: string | null
  readonly tax: TaxAmounts | null
}

export interface DocumentTotal {
  readonly state: DocumentTotalState
  readonly unpricedLineCount: number
  readonly priceUnitNotOrderUnitLineCount: number
  readonly amountOutOfRangeLineCount: number
  readonly netAmount: string | null
  readonly taxState: TaxState
  readonly tax: TaxAmounts | null
}

export interface OrderTotals {
  readonly draftVersion: string
  readonly revision: string
  readonly currencyId: string
  readonly amountScale: number
  readonly taxState: TaxState
  readonly totals: DocumentTotal
  readonly lines: readonly LineTotal[]
}

type Obj = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  if (Object.keys(obj).length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const DECIMAL = /^(0|[1-9][0-9]*)(?:\.([0-9]+))?$/
const isDecimal = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && DECIMAL.test(v)
const oneOf = <T extends string>(values: readonly T[], v: unknown): v is T => typeof v === 'string' && (values as readonly string[]).includes(v)
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 0
/** Mirrors the backend's `Money.MaximumScale` — System.Decimal's own technical limit, not a business policy. */
const MAX_AMOUNT_SCALE = 28

/** An amount padded to exactly `scale` decimals (no sign: totals of an order are never negative). */
function isAmount(v: unknown, scale: number): v is string {
  if (!isDecimal(v)) return false
  const fraction = DECIMAL.exec(v)?.[2] ?? ''
  return fraction.length === scale
}

const TAX_KEYS = ['vatAmount', 'withheldVatAmount', 'grossAmount', 'payableAmount'] as const

/** The four tax amounts: all amounts when the tax is `Computed`, all null otherwise; anything else is malformed (undefined). */
function decodeTax(o: Obj, taxState: TaxState, scale: number): TaxAmounts | null | undefined {
  if (taxState === TOTALS_STATE.computed) {
    return TAX_KEYS.every((key) => isAmount(o[key], scale)) ? {
      vatAmount: o.vatAmount as string,
      withheldVatAmount: o.withheldVatAmount as string,
      grossAmount: o.grossAmount as string,
      payableAmount: o.payableAmount as string,
    } : undefined
  }
  return TAX_KEYS.every((key) => o[key] === null) ? null : undefined
}

const LINE_KEYS = ['lineId', 'lineNo', 'orderUnitId', 'quantity', 'state', 'netUnitPrice', 'netAmount', 'taxState', 'vatPercent',
  'vatAmount', 'withholdingShare', 'withheldVatAmount', 'grossAmount', 'payableAmount'] as const

function decodeLine(value: unknown, scale: number): LineTotal | null {
  const o = exact(value, LINE_KEYS)
  if (o === null || !isReferenceId(o.lineId) || typeof o.lineNo !== 'number' || !Number.isInteger(o.lineNo) || o.lineNo < 1
    || !isReferenceId(o.orderUnitId) || typeof o.quantity !== 'string' || !oneOf(LINE_TOTAL_STATES, o.state)
    || !oneOf(TAX_STATES, o.taxState)) return null
  const computed = o.state === TOTALS_STATE.computed
  if (computed) {
    if (!isDecimal(o.netUnitPrice) || !isAmount(o.netAmount, scale) || o.taxState === TOTALS_STATE.notComputed) return null
  } else if (o.netUnitPrice !== null || o.netAmount !== null || o.taxState !== TOTALS_STATE.notComputed) {
    return null
  }
  const tax = decodeTax(o, o.taxState, scale)
  if (tax === undefined) return null
  const taxComputed = o.taxState === TOTALS_STATE.computed
  if (taxComputed ? !isDecimal(o.vatPercent) || !(o.withholdingShare === null || isDecimal(o.withholdingShare))
    : o.vatPercent !== null || o.withholdingShare !== null) return null
  return {
    lineId: o.lineId, lineNo: o.lineNo, orderUnitId: o.orderUnitId, quantity: o.quantity, state: o.state,
    netUnitPrice: o.netUnitPrice as string | null, netAmount: o.netAmount as string | null, taxState: o.taxState,
    vatPercent: o.vatPercent as string | null, withholdingShare: o.withholdingShare as string | null, tax,
  }
}

const DOCUMENT_KEYS = ['state', 'unpricedLineCount', 'priceUnitNotOrderUnitLineCount', 'amountOutOfRangeLineCount', 'netAmount',
  'taxState', 'vatAmount', 'withheldVatAmount', 'grossAmount', 'payableAmount'] as const

function decodeDocument(value: unknown, scale: number, lines: readonly LineTotal[]): DocumentTotal | null {
  const o = exact(value, DOCUMENT_KEYS)
  if (o === null || !oneOf(DOCUMENT_TOTAL_STATES, o.state) || !isCount(o.unpricedLineCount)
    || !isCount(o.priceUnitNotOrderUnitLineCount) || !isCount(o.amountOutOfRangeLineCount) || !oneOf(TAX_STATES, o.taxState)) return null
  const unpriced = lines.filter((line) => line.state === TOTALS_STATE.unpriced).length
  const unconvertible = lines.filter((line) => line.state === TOTALS_STATE.priceUnitNotOrderUnit).length
  const outOfRange = lines.filter((line) => line.state === TOTALS_STATE.amountOutOfRange).length
  if (o.unpricedLineCount !== unpriced || o.priceUnitNotOrderUnitLineCount !== unconvertible
    || o.amountOutOfRangeLineCount !== outOfRange) return null
  // No line is nothing to total (the backend's rule): Incomplete with every count 0, never a computed 0.
  const everyLineHasAmount = lines.length > 0 && unpriced === 0 && unconvertible === 0 && outOfRange === 0
  if (everyLineHasAmount === (o.state === TOTALS_STATE.incomplete)) return null
  if (o.state === TOTALS_STATE.computed) {
    if (!isAmount(o.netAmount, scale) || o.taxState === TOTALS_STATE.notComputed) return null
    // The document tax is computed only when every line's is (the sums are the lines').
    const everyLineTaxed = lines.every((line) => line.taxState === TOTALS_STATE.computed)
    if ((o.taxState === TOTALS_STATE.computed) !== everyLineTaxed) return null
  } else if (o.netAmount !== null || o.taxState !== TOTALS_STATE.notComputed) {
    return null
  }
  const tax = decodeTax(o, o.taxState, scale)
  if (tax === undefined) return null
  return {
    state: o.state, unpricedLineCount: o.unpricedLineCount, priceUnitNotOrderUnitLineCount: o.priceUnitNotOrderUnitLineCount,
    amountOutOfRangeLineCount: o.amountOutOfRangeLineCount, netAmount: o.netAmount as string | null, taxState: o.taxState, tax,
  }
}

const RESPONSE_KEYS = ['draftVersion', 'revision', 'currencyId', 'amountScale', 'taxState', 'totals', 'lines'] as const

/**
 * The totals of `detail`, or null when the body is malformed or describes anything but exactly that detail (another
 * version, revision or currency; a line missing, repeated, renumbered, in another unit or of another quantity).
 */
export function decodeOrderTotals(value: unknown, detail: DraftDetail): OrderTotals | null {
  const o = exact(value, RESPONSE_KEYS)
  if (o === null || o.draftVersion !== detail.draft.version || o.revision !== detail.draft.revision
    || typeof o.currencyId !== 'string' || o.currencyId.toLowerCase() !== detail.draft.currencyId.toLowerCase() || typeof o.amountScale !== 'number' || !Number.isInteger(o.amountScale)
    || o.amountScale < 0 || o.amountScale > MAX_AMOUNT_SCALE || !oneOf(TAX_STATES, o.taxState) || o.taxState === TOTALS_STATE.notComputed
    || !Array.isArray(o.lines) || o.lines.length !== detail.lines.length) return null
  const scale = o.amountScale
  // Identities compare case-insensitively (a GUID's text case is not identity); the lines are shown in the detail's order.
  const byId = new Map<string, LineTotal>()
  for (const raw of o.lines) {
    const line = decodeLine(raw, scale)
    if (line === null || byId.has(line.lineId.toLowerCase())) return null
    // Without a VAT master no line can carry a computed tax.
    if (o.taxState === TOTALS_STATE.noSource && line.taxState === TOTALS_STATE.computed) return null
    byId.set(line.lineId.toLowerCase(), line)
  }
  const lines: LineTotal[] = []
  for (const expected of detail.lines) {
    const line = byId.get(expected.lineId.toLowerCase())
    if (line === undefined || line.lineNo !== expected.lineNo || line.orderUnitId.toLowerCase() !== expected.orderUnitId.toLowerCase()
      || line.quantity !== expected.quantity) return null
    lines.push({ ...line, lineId: expected.lineId, orderUnitId: expected.orderUnitId })
  }
  const totals = decodeDocument(o.totals, scale, lines)
  if (totals === null) return null
  return {
    draftVersion: o.draftVersion, revision: o.revision, currencyId: detail.draft.currencyId, amountScale: scale, taxState: o.taxState,
    totals, lines,
  }
}

/** An order without lines: nothing is read and nothing is totalled (the backend answers it `Incomplete` with every count 0). */
export const NO_LINES = { type: 'no-lines' } as const

/**
 * What the card shows for `detail`: an order without lines is the neutral "no lines" state (nothing to total, and no line
 * says whether prices are disclosed — never "forbidden"); withheld prices are the forbidden state without a read; otherwise
 * the read's outcome. The S-04 detail decoder refuses a zero-line detail today (`lineCount` ≥ 1), so the first branch is the
 * card's own guard, kept in step with the backend's zero-line rule.
 */
export function cardOutcome(
  detail: DraftDetail,
  read: ViewOutcome<OrderTotals> | undefined,
): ViewOutcome<OrderTotals> | typeof NO_LINES | undefined {
  if (detail.lines.length === 0) return NO_LINES
  return detail.lines.every((line) => line.priceDisclosure === 'Visible') ? read : { type: 'http', status: 403 }
}

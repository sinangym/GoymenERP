/**
 * SAT-D436 / SAT-D439 (AK-5): the kg price indicator — `GET /sales/drafts/{id}/kilogram-prices`
 * (`SalesOrderKilogramPricesResponse`) and the invoice detail's `kilogramPrices` (`SalesInvoiceKilogramPricesV1`).
 *
 * Strict and fail-closed like the other Sales decoders: exact member sets (optional members are the server's
 * `WhenWritingNull` ones), every figure stays the server's TEXT, and an unknown state makes the whole answer `null`.
 * The screen never divides an amount by a kg: the figures come from the backend calculator only.
 */
import { isReferenceId } from './wirePrimitives'

type Obj = Record<string, unknown>

function exactWithOptional(value: unknown, required: readonly string[], optional: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  const allowed = new Set([...required, ...optional])
  for (const key of Object.keys(obj)) if (!allowed.has(key)) return null
  for (const key of required) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const AMOUNT_RE = /^\d+(?:\.\d+)?$/
const SIGNED_RE = /^-?\d+(?:\.\d+)?$/
const PRICE_RE = /^\d+\.\d{2}$/
const isAmount = (value: unknown): value is string => typeof value === 'string' && value.length <= 64 && AMOUNT_RE.test(value)
const isSigned = (value: unknown): value is string => typeof value === 'string' && value.length <= 64 && SIGNED_RE.test(value)
const nullable = (value: unknown, check: (v: unknown) => boolean) => value === null || check(value)

export const KILOGRAM_PRICE_STATES = [
  'Shown', 'TheoreticalKgUnavailable', 'WeighedKgUnavailable', 'ZeroKg', 'AmountUnavailable', 'DocumentNotAllKilogramLines',
  'OutOfRange',
] as const
export type KilogramPriceState = (typeof KILOGRAM_PRICE_STATES)[number]

export const KILOGRAM_PRICE_READ_STATES = ['Computed', 'FormulaGoldenPending'] as const
export type KilogramPriceReadState = (typeof KILOGRAM_PRICE_READ_STATES)[number]

export interface KilogramPrice {
  readonly state: KilogramPriceState
  /** Set exactly when `state` is `Shown`: the document currency per kg at 0,01 (SAT-D439: no rate). */
  readonly value: string | null
}

export function decodeKilogramPrice(value: unknown): KilogramPrice | null {
  const o = exactWithOptional(value, ['state'], ['value'])
  if (o === null || typeof o.state !== 'string' || !(KILOGRAM_PRICE_STATES as readonly string[]).includes(o.state)) return null
  const state = o.state as KilogramPriceState
  if (state === 'Shown') {
    return typeof o.value === 'string' && PRICE_RE.test(o.value) ? { state, value: o.value } : null
  }
  return o.value === undefined ? { state, value: null } : null
}

export interface OrderLineKilogramPrices {
  readonly lineId: string
  readonly lineNo: number
  readonly theoreticalKg: string | null
  readonly theoreticalPrice: KilogramPrice
  readonly invoicedQuantity: string
  readonly invoicedAmount: string | null
  readonly actualKg: string | null
  readonly actualPrice: KilogramPrice
  readonly kgDifference: string | null
  readonly deviationPercent: string | null
}

export interface OrderKilogramPrices {
  readonly state: KilogramPriceReadState
  readonly currencyId: string
  readonly lines: readonly OrderLineKilogramPrices[] | null
  readonly documentTheoreticalPrice: KilogramPrice | null
  readonly documentActualPrice: KilogramPrice | null
}

function decodeOrderLine(value: unknown): OrderLineKilogramPrices | null {
  const o = exactWithOptional(value, ['lineId', 'lineNo', 'theoreticalKg', 'theoreticalPrice', 'invoicedQuantity', 'invoicedAmount',
    'actualKg', 'actualPrice', 'kgDifference', 'deviationPercent'], [])
  if (
    o === null || !isReferenceId(o.lineId) || typeof o.lineNo !== 'number' || !Number.isSafeInteger(o.lineNo) || o.lineNo < 1
    || !isAmount(o.invoicedQuantity) || !nullable(o.theoreticalKg, isAmount) || !nullable(o.invoicedAmount, isAmount)
    || !nullable(o.actualKg, isAmount) || !nullable(o.kgDifference, isSigned) || !nullable(o.deviationPercent, isSigned)
  ) return null
  const theoreticalPrice = decodeKilogramPrice(o.theoreticalPrice)
  const actualPrice = decodeKilogramPrice(o.actualPrice)
  if (theoreticalPrice === null || actualPrice === null) return null
  const text = (v: unknown) => (typeof v === 'string' ? v : null)
  return {
    lineId: o.lineId, lineNo: o.lineNo, theoreticalKg: text(o.theoreticalKg), theoreticalPrice,
    invoicedQuantity: o.invoicedQuantity, invoicedAmount: text(o.invoicedAmount), actualKg: text(o.actualKg), actualPrice,
    kgDifference: text(o.kgDifference), deviationPercent: text(o.deviationPercent),
  }
}

/** The order read for exactly `currencyId`'s order; a pending read carries nothing but its state. */
export function decodeOrderKilogramPrices(value: unknown): OrderKilogramPrices | null {
  const o = exactWithOptional(value, ['state', 'currencyId'], ['lines', 'documentTheoreticalPrice', 'documentActualPrice'])
  if (o === null || !isReferenceId(o.currencyId)) return null
  if (o.state === 'FormulaGoldenPending') {
    return o.lines === undefined && o.documentTheoreticalPrice === undefined && o.documentActualPrice === undefined
      ? { state: 'FormulaGoldenPending', currencyId: o.currencyId, lines: null, documentTheoreticalPrice: null, documentActualPrice: null }
      : null
  }
  if (o.state !== 'Computed' || !Array.isArray(o.lines)) return null
  const lines: OrderLineKilogramPrices[] = []
  for (const entry of o.lines) {
    const line = decodeOrderLine(entry)
    if (line === null) return null
    lines.push(line)
  }
  const documentTheoreticalPrice = decodeKilogramPrice(o.documentTheoreticalPrice)
  const documentActualPrice = decodeKilogramPrice(o.documentActualPrice)
  if (documentTheoreticalPrice === null || documentActualPrice === null) return null
  return { state: 'Computed', currencyId: o.currencyId, lines, documentTheoreticalPrice, documentActualPrice }
}

export interface InvoiceLineKilogramPrices {
  readonly lineId: string
  readonly theoreticalPrice: KilogramPrice
  readonly actualPrice: KilogramPrice
}

export interface InvoiceKilogramPrices {
  readonly state: KilogramPriceReadState
  readonly lines: readonly InvoiceLineKilogramPrices[] | null
  readonly documentTheoreticalPrice: KilogramPrice | null
  readonly documentActualPrice: KilogramPrice | null
}

export function decodeInvoiceKilogramPrices(value: unknown): InvoiceKilogramPrices | null {
  const o = exactWithOptional(value, ['state'], ['lines', 'documentTheoreticalPrice', 'documentActualPrice'])
  if (o === null) return null
  if (o.state === 'FormulaGoldenPending') {
    return o.lines === undefined && o.documentTheoreticalPrice === undefined && o.documentActualPrice === undefined
      ? { state: 'FormulaGoldenPending', lines: null, documentTheoreticalPrice: null, documentActualPrice: null }
      : null
  }
  if (o.state !== 'Computed' || !Array.isArray(o.lines)) return null
  const lines: InvoiceLineKilogramPrices[] = []
  for (const entry of o.lines) {
    const line = exactWithOptional(entry, ['lineId', 'theoreticalPrice', 'actualPrice'], [])
    if (line === null || !isReferenceId(line.lineId)) return null
    const theoreticalPrice = decodeKilogramPrice(line.theoreticalPrice)
    const actualPrice = decodeKilogramPrice(line.actualPrice)
    if (theoreticalPrice === null || actualPrice === null) return null
    lines.push({ lineId: line.lineId, theoreticalPrice, actualPrice })
  }
  const documentTheoreticalPrice = decodeKilogramPrice(o.documentTheoreticalPrice)
  const documentActualPrice = decodeKilogramPrice(o.documentActualPrice)
  if (documentTheoreticalPrice === null || documentActualPrice === null) return null
  return { state: 'Computed', lines, documentTheoreticalPrice, documentActualPrice }
}

export const salesDraftKilogramPricesPath = (draftId: string) => `/sales/drafts/${encodeURIComponent(draftId)}/kilogram-prices`

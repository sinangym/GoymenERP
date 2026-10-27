/**
 * S-01 forms — the sales request create, the sales quote create and the quote revision: values, validation and the one
 * normalized wire body each. Pure, so every rule is tested without a page.
 *
 * THE RULES (the order draft form's, ./orderDraftForm.ts, where they apply):
 *  - every reference (Cari account, currency, product + its version, order unit, price unit) is CHOSEN from what an owner
 *    offers, or comes from the stored record the line was copied from (a request line, the quote's current line) — never
 *    typed;
 *  - `lineId` is a client-owned UUID minted ONCE when the row is added (or kept from the quote's line in a revision), so a
 *    retry sends the same identities;
 *  - quantities, prices and discounts stay text end to end; an empty optional value is null (unknown), never zero, never
 *    today;
 *  - no open policy is chosen here: no default validity period, no discount limit, no price permission. The server
 *    judges a discount range or a price scale and its answer is shown on the field it names.
 */

import { LIMITS } from './contract'
import type { OrderDraftOptions } from './orderDraftForm'
import {
  QUOTE_MAXIMUM_REQUESTS,
  type CreateSalesQuoteBody,
  type CreateSalesRequestBody,
  type SalesQuoteLine,
  type SalesQuoteLineBody,
  type SalesQuoteRevisionBody,
  type SalesRequestLine,
  type SalesRequestLineBody,
} from './poolContract'
import { POOL_TEXT } from './poolText'
import { isDateOnly, parseQuantityInput, type QuantityInputError } from './wirePrimitives'

export interface PoolUnitOption {
  readonly unitId: string
  readonly code: string
}

export interface PoolLineValues {
  /** The wire `lineId`, minted once (or the stored line's own id in a revision). Also the React key and field-id stem. */
  readonly lineId: string
  readonly productId: string
  /** The product version the line expects: the offered option's, or the stored line's snapshot. */
  readonly productVersion: string
  readonly productLabel: string
  /** The units this line may use: the offered product's order units, or the stored line's own unit. */
  readonly units: readonly PoolUnitOption[]
  /** True when the product and unit come from a stored record and are not re-chosen here. */
  readonly fixedProduct: boolean
  /**
   * The stored line's price is withheld from this viewer (`sales.orders.price.read`): the form cannot carry it, and a
   * revision replaces every line — so such a row is never sent (it would write the price away).
   */
  readonly priceWithheld: boolean
  /** The stored price unit when it is not one of `units` (a revision row), with its label; null otherwise. */
  readonly storedPriceUnit: PoolUnitOption | null
  readonly orderUnitId: string
  readonly quantityText: string
  readonly unitPriceText: string
  readonly priceUnitId: string
  readonly discountText: string
  readonly requestedDeliveryDate: string
  readonly note: string
}

export interface PoolFormValues {
  readonly partyId: string
  readonly companyAccountId: string
  readonly businessDate: string
  readonly currencyId: string
  readonly validUntil: string
  readonly requestedDeliveryDate: string
  readonly note: string
  readonly lines: readonly PoolLineValues[]
}

export const POOL_FIELD = {
  customer: 'customer',
  businessDate: 'businessDate',
  currency: 'currency',
  validUntil: 'validUntil',
  requestedDeliveryDate: 'requestedDeliveryDate',
  note: 'note',
  lines: 'lines',
  requests: 'requests',
} as const

export type PoolLineField =
  | 'productId'
  | 'orderUnitId'
  | 'quantity'
  | 'unitPrice'
  | 'priceUnitId'
  | 'discount'
  | 'requestedDeliveryDate'
  | 'note'

export const poolLineFieldId = (lineId: string, field: PoolLineField) => `lines.${lineId}.${field}`
/** A line-level error (a rule on the whole row). */
export const poolLineRowId = (lineId: string) => `lines.${lineId}`

export type PoolFormErrors = Readonly<Record<string, string>>

export type PoolValidation<B> = { readonly ok: true; readonly payload: B } | { readonly ok: false; readonly errors: PoolFormErrors }

export function emptyPoolLine(lineId: string): PoolLineValues {
  return {
    lineId,
    productId: '',
    productVersion: '',
    productLabel: '',
    units: [],
    fixedProduct: false,
    priceWithheld: false,
    storedPriceUnit: null,
    orderUnitId: '',
    quantityText: '',
    unitPriceText: '',
    priceUnitId: '',
    discountText: '',
    requestedDeliveryDate: '',
    note: '',
  }
}

export function emptyPoolForm(firstLineId: string): PoolFormValues {
  return {
    partyId: '',
    companyAccountId: '',
    businessDate: '',
    currencyId: '',
    validUntil: '',
    requestedDeliveryDate: '',
    note: '',
    lines: [emptyPoolLine(firstLineId)],
  }
}

type ProductOption = OrderDraftOptions['products'][number]

/** Choose an offered product for a line: its version and units follow; a unit the product does not offer is cleared. */
export function withProduct(line: PoolLineValues, product: ProductOption | undefined): PoolLineValues {
  if (product === undefined) {
    return { ...line, productId: '', productVersion: '', productLabel: '', units: [], orderUnitId: '', priceUnitId: '' }
  }
  const units = product.orderUnits.map((unit) => ({ unitId: unit.unitId, code: unit.code }))
  const keep = (id: string) => (units.some((unit) => unit.unitId === id) ? id : '')
  return {
    ...line,
    productId: product.productId,
    productVersion: product.productVersion,
    productLabel: product.label,
    units,
    fixedProduct: false,
    orderUnitId: keep(line.orderUnitId),
    priceUnitId: keep(line.priceUnitId),
  }
}

/** A quote line copied from a request line (No 163): product, version, unit, quantity, date and note as the request has them. */
export function lineFromRequest(lineId: string, line: SalesRequestLine): PoolLineValues {
  return {
    ...emptyPoolLine(lineId),
    productId: line.productId,
    productVersion: line.productVersion,
    productLabel: `${line.productCode} — ${line.productName}`,
    units: [{ unitId: line.orderUnitId, code: line.orderUnitCode }],
    fixedProduct: true,
    orderUnitId: line.orderUnitId,
    quantityText: line.quantity,
    requestedDeliveryDate: line.requestedDeliveryDate ?? '',
    note: line.note ?? '',
  }
}

/**
 * A revision row from the quote's current line, under its own line id. A withheld price is marked, never emptied into
 * "no price": `validateQuoteRevision` refuses such a row (fail closed). `unitCodes` labels a stored price unit that is
 * not the line's order unit (the quote's own order-unit codes); one it does not know reads as "the stored price unit".
 */
export function lineFromQuote(line: SalesQuoteLine, unitCodes: ReadonlyMap<string, string> = new Map()): PoolLineValues {
  const units: PoolUnitOption[] = [{ unitId: line.orderUnitId, code: line.orderUnitCode }]
  const storedPriceUnit =
    line.priceUnitId === null || line.priceUnitId === line.orderUnitId
      ? null
      : { unitId: line.priceUnitId, code: unitCodes.get(line.priceUnitId) ?? POOL_TEXT.form.storedPriceUnit }
  return {
    ...emptyPoolLine(line.lineId),
    productId: line.productId,
    productVersion: line.productVersion,
    productLabel: `${line.productCode} — ${line.productName}`,
    units,
    fixedProduct: true,
    priceWithheld: line.priceDisclosure === 'Withheld',
    storedPriceUnit,
    orderUnitId: line.orderUnitId,
    quantityText: line.quantity,
    unitPriceText: line.unitPrice ?? '',
    // The stored price unit is offered back even when it is not the order unit (it is the stored choice).
    priceUnitId: line.priceUnitId ?? '',
    discountText: line.discountPercent ?? '',
    requestedDeliveryDate: line.requestedDeliveryDate ?? '',
    note: line.note ?? '',
  }
}

/** P1: whether any current line's price is withheld from this viewer — then no revision may be sent from this screen. */
export const hasWithheldPrice = (detail: { readonly lines: readonly Pick<SalesQuoteLine, 'priceDisclosure'>[] }): boolean =>
  detail.lines.some((line) => line.priceDisclosure === 'Withheld')

/** The price units a line may name: its own units plus, in a revision, the stored price unit. */
export function priceUnitChoices(line: PoolLineValues): readonly PoolUnitOption[] {
  const stored = line.storedPriceUnit
  if (stored === null || line.units.some((unit) => unit.unitId === stored.unitId)) return line.units
  return [...line.units, stored]
}

const QUANTITY_MESSAGE: Record<QuantityInputError, string> = {
  required: POOL_TEXT.validation.quantityRequired,
  invalid: POOL_TEXT.validation.quantityInvalid,
  'ambiguous-separator': POOL_TEXT.validation.quantityAmbiguous,
  'not-positive': POOL_TEXT.validation.quantityNotPositive,
}

/**
 * A non-negative decimal as typed, canonicalized as text (the quantity parser's rules: `,` or `.` as THE separator, the
 * ambiguous `1.000` refused). Zero is a value here — `0` is not "unknown" — so it is kept; empty is null.
 */
export function parseOptionalDecimal(raw: string): { readonly ok: true; readonly value: string | null } | { readonly ok: false; readonly error: QuantityInputError } {
  const text = raw.trim()
  if (text === '') return { ok: true, value: null }
  const parsed = parseQuantityInput(text)
  if (parsed.ok) return { ok: true, value: parsed.value }
  if (parsed.error === 'not-positive') return { ok: true, value: '0' }
  return { ok: false, error: parsed.error }
}

const noteOrNull = (note: string) => (note.trim() === '' ? null : note)
const dateOrNull = (date: string) => (date === '' ? null : date)

function validateHeader(
  values: PoolFormValues,
  accounts: OrderDraftOptions['accounts'],
  errors: Record<string, string>,
): void {
  const text = POOL_TEXT.validation
  if (values.partyId === '' || values.companyAccountId === '') errors[POOL_FIELD.customer] = text.customerRequired
  else if (!accounts.some((a) => a.partyId === values.partyId && a.companyAccountId === values.companyAccountId)) {
    errors[POOL_FIELD.customer] = text.customerNotOffered
  }
  if (values.businessDate === '') errors[POOL_FIELD.businessDate] = text.businessDateRequired
  else if (!isDateOnly(values.businessDate)) errors[POOL_FIELD.businessDate] = text.dateInvalid
  if (values.requestedDeliveryDate !== '' && !isDateOnly(values.requestedDeliveryDate)) {
    errors[POOL_FIELD.requestedDeliveryDate] = text.dateInvalid
  }
  if (values.note.length > LIMITS.note) errors[POOL_FIELD.note] = text.noteTooLong
}

interface LineCore {
  readonly lineId: string
  readonly productId: string
  readonly expectedProductVersion: string
  readonly orderUnitId: string
  readonly quantity: string
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
}

/** The product, unit, quantity, date and note of every line; `null` where the line has an error. */
function validateLineCores(lines: readonly PoolLineValues[], errors: Record<string, string>): readonly (LineCore | null)[] {
  const text = POOL_TEXT.validation
  if (lines.length === 0) errors[POOL_FIELD.lines] = text.linesRequired
  else if (lines.length > LIMITS.lines) errors[POOL_FIELD.lines] = text.tooManyLines
  return lines.map((line) => {
    let ok = true
    const fail = (field: PoolLineField, message: string) => {
      errors[poolLineFieldId(line.lineId, field)] = message
      ok = false
    }
    if (line.productId === '' || line.productVersion === '') fail('productId', text.productRequired)
    if (line.orderUnitId === '') fail('orderUnitId', text.unitRequired)
    else if (!line.units.some((unit) => unit.unitId === line.orderUnitId)) fail('orderUnitId', text.unitNotOffered)
    const quantity = parseQuantityInput(line.quantityText)
    if (!quantity.ok) fail('quantity', QUANTITY_MESSAGE[quantity.error])
    if (line.requestedDeliveryDate !== '' && !isDateOnly(line.requestedDeliveryDate)) fail('requestedDeliveryDate', text.dateInvalid)
    if (line.note.length > LIMITS.note) fail('note', text.noteTooLong)
    if (!ok || !quantity.ok) return null
    return {
      lineId: line.lineId,
      productId: line.productId,
      expectedProductVersion: line.productVersion,
      orderUnitId: line.orderUnitId,
      quantity: quantity.value,
      requestedDeliveryDate: dateOrNull(line.requestedDeliveryDate),
      note: noteOrNull(line.note),
    }
  })
}

/** API-20: the request create. No price, no currency, no validity: a request carries none. */
export function validateRequestForm(
  values: PoolFormValues,
  accounts: OrderDraftOptions['accounts'],
): PoolValidation<CreateSalesRequestBody> {
  const errors: Record<string, string> = {}
  validateHeader(values, accounts, errors)
  const cores = validateLineCores(values.lines, errors)
  if (Object.keys(errors).length > 0) return { ok: false, errors }
  const lines: SalesRequestLineBody[] = cores.map((core) => core as LineCore)
  return {
    ok: true,
    payload: {
      partyId: values.partyId,
      companyAccountId: values.companyAccountId,
      businessDate: values.businessDate,
      lines,
      requestedDeliveryDate: dateOrNull(values.requestedDeliveryDate),
      note: noteOrNull(values.note),
    },
  }
}

/** The commercial half of the quote lines: price with its own unit, a discount only beside a price. */
function validateQuoteLines(lines: readonly PoolLineValues[], errors: Record<string, string>): readonly SalesQuoteLineBody[] | null {
  const text = POOL_TEXT.validation
  const cores = validateLineCores(lines, errors)
  const bodies: SalesQuoteLineBody[] = []
  lines.forEach((line, index) => {
    const price = parseOptionalDecimal(line.unitPriceText)
    const discount = parseOptionalDecimal(line.discountText)
    let ok = true
    if (!price.ok) {
      errors[poolLineFieldId(line.lineId, 'unitPrice')] = text.priceInvalid
      ok = false
    }
    if (!discount.ok) {
      errors[poolLineFieldId(line.lineId, 'discount')] = text.discountInvalid
      ok = false
    }
    const unitPrice = price.ok ? price.value : null
    const discountPercent = discount.ok ? discount.value : null
    if (price.ok && unitPrice !== null && line.priceUnitId === '') {
      errors[poolLineFieldId(line.lineId, 'priceUnitId')] = text.priceUnitRequired
      ok = false
    }
    if (price.ok && discount.ok && unitPrice === null && discountPercent !== null) {
      errors[poolLineFieldId(line.lineId, 'discount')] = text.discountWithoutPrice
      ok = false
    }
    // Fail closed: a row whose stored price this viewer cannot see would be sent without it and erase it.
    if (line.priceWithheld) {
      errors[poolLineRowId(line.lineId)] = text.priceWithheldRevision
      ok = false
    }
    const core = cores[index]
    if (!ok || core === null || core === undefined) return
    bodies.push({
      lineId: core.lineId,
      productId: core.productId,
      expectedProductVersion: core.expectedProductVersion,
      orderUnitId: core.orderUnitId,
      quantity: core.quantity,
      unitPrice,
      priceUnitId: unitPrice === null ? null : line.priceUnitId,
      discountPercent,
      requestedDeliveryDate: core.requestedDeliveryDate,
      note: core.note,
    })
  })
  return Object.keys(errors).length > 0 ? null : bodies
}

function validateValidUntil(values: PoolFormValues, errors: Record<string, string>): void {
  const text = POOL_TEXT.validation
  if (values.validUntil === '') return
  if (!isDateOnly(values.validUntil)) errors[POOL_FIELD.validUntil] = text.dateInvalid
  // The contract's own rule ("must not precede the business date"); no validity period is chosen.
  else if (isDateOnly(values.businessDate) && values.validUntil < values.businessDate) {
    errors[POOL_FIELD.validUntil] = text.validUntilBeforeDate
  }
}

/** API-22: the quote create, covering `requestIds` (null when none). */
export function validateQuoteForm(
  values: PoolFormValues,
  accounts: OrderDraftOptions['accounts'],
  currencies: OrderDraftOptions['currencies'],
  requestIds: readonly string[],
): PoolValidation<CreateSalesQuoteBody> {
  const text = POOL_TEXT.validation
  const errors: Record<string, string> = {}
  validateHeader(values, accounts, errors)
  if (values.currencyId === '') errors[POOL_FIELD.currency] = text.currencyRequired
  else if (!currencies.some((c) => c.currencyId === values.currencyId)) errors[POOL_FIELD.currency] = text.currencyNotOffered
  validateValidUntil(values, errors)
  if (requestIds.length > QUOTE_MAXIMUM_REQUESTS) errors[POOL_FIELD.requests] = POOL_TEXT.quoteCreate.requestsTooMany
  const lines = validateQuoteLines(values.lines, errors)
  if (lines === null || Object.keys(errors).length > 0) return { ok: false, errors }
  return {
    ok: true,
    payload: {
      partyId: values.partyId,
      companyAccountId: values.companyAccountId,
      currencyId: values.currencyId,
      businessDate: values.businessDate,
      lines,
      requestIds: requestIds.length === 0 ? null : [...requestIds],
      validUntil: dateOrNull(values.validUntil),
      requestedDeliveryDate: dateOrNull(values.requestedDeliveryDate),
      note: noteOrNull(values.note),
    },
  }
}

/** API-22 revision: every line and the terms; the customer, currency, date and requests stay as stored. */
export function validateQuoteRevision(values: PoolFormValues, expectedVersion: string): PoolValidation<SalesQuoteRevisionBody> {
  const errors: Record<string, string> = {}
  if (values.requestedDeliveryDate !== '' && !isDateOnly(values.requestedDeliveryDate)) {
    errors[POOL_FIELD.requestedDeliveryDate] = POOL_TEXT.validation.dateInvalid
  }
  if (values.note.length > LIMITS.note) errors[POOL_FIELD.note] = POOL_TEXT.validation.noteTooLong
  validateValidUntil(values, errors)
  const lines = validateQuoteLines(values.lines, errors)
  if (lines === null || Object.keys(errors).length > 0) return { ok: false, errors }
  return {
    ok: true,
    payload: {
      expectedVersion,
      lines,
      validUntil: dateOrNull(values.validUntil),
      requestedDeliveryDate: dateOrNull(values.requestedDeliveryDate),
      note: noteOrNull(values.note),
    },
  }
}

/**
 * Where a server field error belongs on these forms. Line paths are resolved against the SENT snapshot; a path these
 * forms do not render (e.g. `quote`) returns null and the caller shows it in the summary, so nothing goes silent.
 */
export function poolFieldForServerPath(path: string, sentLines: readonly { readonly lineId: string }[]): string | null {
  if (path === 'partyId' || path === 'companyAccountId') return POOL_FIELD.customer
  if (path === 'currencyId') return POOL_FIELD.currency
  if (path === 'businessDate') return POOL_FIELD.businessDate
  if (path === 'validUntil') return POOL_FIELD.validUntil
  if (path === 'requestedDeliveryDate') return POOL_FIELD.requestedDeliveryDate
  if (path === 'note') return POOL_FIELD.note
  if (path === 'lines') return POOL_FIELD.lines
  if (path === 'requestIds' || /^requestIds\[\d+\]$/.test(path)) return POOL_FIELD.requests
  const match = /^lines\[(\d+)\](?:\.(productId|expectedProductVersion|orderUnitId|quantity|unitPrice|priceUnitId|discountPercent|requestedDeliveryDate|note))?$/.exec(path)
  if (match === null) return null
  const line = sentLines[Number(match[1])]
  if (line === undefined) return null
  const member = match[2]
  // A rule the server names on the whole line (`lines[i]`, e.g. a discount range) belongs to the row, not a field.
  if (member === undefined) return poolLineRowId(line.lineId)
  const field: PoolLineField =
    member === 'productId' || member === 'expectedProductVersion'
      ? 'productId'
      : member === 'discountPercent'
        ? 'discount'
        : (member as PoolLineField)
  return poolLineFieldId(line.lineId, field)
}

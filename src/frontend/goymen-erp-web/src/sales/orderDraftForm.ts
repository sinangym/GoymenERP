/**
 * Sales order draft form: values, validation and the one normalized create body.
 *
 * Source: `SALES_ORDER_DRAFT_JIT_SPEC_v1.md` §2–§3 (#254 @ 290624d). Wire names live
 * in ./contract.ts; this file never spells one the contract does not.
 *
 * THE RULES THIS FORM ENCODES, and where each comes from:
 *  - every reference (Cari account, currency, product, order unit) is CHOSEN from a
 *    server projection and never typed. A reference the projection no longer offers
 *    is refused client-side exactly like an invented one: a passive account (AT-013),
 *    an account of another company, a Draft product or a unit the product does not
 *    offer are all simply absent from what the owner offers (§3, §5);
 *  - the company is NOT a form value. It comes from the committed shell context and
 *    travels as request context, never in the body (§2);
 *  - a quantity stays text end to end and is canonicalized losslessly;
 *  - an empty due date is null — unknown — and never becomes today (§2);
 *  - nothing is priced, totalled or converted here. §2: without an approved price,
 *    tax, rate and discount order, no total or margin is produced.
 */

import { ORDER_KIND, type OrderDraftCreateRequest, type OrderDraftLineRequest } from './contract'
import { ORDER_DRAFT_TEXT } from './salesText'
import { isDateOnly, parseQuantityInput, type QuantityInputError } from './wirePrimitives'

/** What the owners currently offer for selection. Filled from projections the Backend pins. */
export interface OrderDraftOptions {
  /** Active Party + an active account of the COMMITTED company, as offered together. */
  readonly accounts: readonly { readonly partyId: string; readonly companyAccountId: string }[]
  readonly currencyIds: readonly string[]
  /** Products the Product owner reports as usable for sales. Draft/Sellable alone is not. */
  readonly productIds: readonly string[]
  /** Order units the owner offers per product. A unit is never assumed to be kg. */
  readonly unitIdsByProduct: Readonly<Record<string, readonly string[]>>
}

export interface OrderDraftLineValues {
  /**
   * Local, stable identity of the row: React key and field-id stem. It is NOT the
   * wire line id — who mints that is still a Backend pin (§2).
   */
  readonly key: string
  readonly productId: string
  readonly quantityText: string
  readonly orderUnitId: string
  /** `YYYY-MM-DD` from a date input, or `''`. */
  readonly requestedDeliveryDate: string
}

export interface OrderDraftFormValues {
  /** Both empty, or both from one offered account entry. */
  readonly partyId: string
  readonly companyAccountId: string
  readonly businessDate: string
  readonly currencyId: string
  readonly lines: readonly OrderDraftLineValues[]
}

export type LineField = 'productId' | 'quantity' | 'orderUnitId' | 'requestedDeliveryDate'

/** Field ids shared by inputs, the error summary and (later) the server-error mapper. */
export const ORDER_FIELD = {
  customer: 'customer',
  businessDate: 'businessDate',
  currency: 'currency',
  lines: 'lines',
} as const

export function lineFieldId(lineKey: string, field: LineField): string {
  return `lines.${lineKey}.${field}`
}

export type OrderDraftErrors = Readonly<Record<string, string>>

export type OrderDraftValidation =
  | { readonly ok: true; readonly payload: OrderDraftCreateRequest }
  | { readonly ok: false; readonly errors: OrderDraftErrors }

export function emptyLine(key: string): OrderDraftLineValues {
  return { key, productId: '', quantityText: '', orderUnitId: '', requestedDeliveryDate: '' }
}

/**
 * The starting form: one blank line, because the first positive path needs at least
 * one goods line. The business date is left blank on purpose — "today" in the
 * browser's zone is not necessarily the company's business day, and the spec keeps
 * business date and record time apart.
 */
export function emptyOrderDraftForm(firstLineKey: string): OrderDraftFormValues {
  return {
    partyId: '',
    companyAccountId: '',
    businessDate: '',
    currencyId: '',
    lines: [emptyLine(firstLineKey)],
  }
}

/** Nothing typed or chosen yet. Drives the unsaved and company-switch guards. */
export function isOrderDraftPristine(values: OrderDraftFormValues): boolean {
  return (
    values.partyId === '' &&
    values.companyAccountId === '' &&
    values.businessDate === '' &&
    values.currencyId === '' &&
    values.lines.every(
      (line) =>
        line.productId === '' &&
        line.quantityText.trim() === '' &&
        line.orderUnitId === '' &&
        line.requestedDeliveryDate === '',
    )
  )
}

export function addLine(values: OrderDraftFormValues, key: string): OrderDraftFormValues {
  return { ...values, lines: [...values.lines, emptyLine(key)] }
}

export function removeLine(values: OrderDraftFormValues, key: string): OrderDraftFormValues {
  return { ...values, lines: values.lines.filter((line) => line.key !== key) }
}

/**
 * Choosing another product clears the line's unit.
 *
 * Units are offered PER product; a unit chosen for the previous product riding along
 * onto the new one is a reference nobody offered for it.
 */
export function setLineProduct(
  values: OrderDraftFormValues,
  key: string,
  productId: string,
): OrderDraftFormValues {
  return {
    ...values,
    lines: values.lines.map((line) =>
      line.key !== key || line.productId === productId
        ? line
        : { ...line, productId, orderUnitId: '' },
    ),
  }
}

const QUANTITY_MESSAGE: Record<QuantityInputError, string> = {
  required: ORDER_DRAFT_TEXT.validation.quantityRequired,
  invalid: ORDER_DRAFT_TEXT.validation.quantityInvalid,
  'ambiguous-separator': ORDER_DRAFT_TEXT.validation.quantityAmbiguous,
  'not-positive': ORDER_DRAFT_TEXT.validation.quantityNotPositive,
}

/**
 * Validate and normalize. Errors are keyed in DOM order: header first, then each
 * line's fields in the order they are rendered.
 *
 * MISSING and NOT OFFERED are different messages on purpose: the first asks for a
 * choice, the second says a choice the person made is no longer available — which
 * is what a passive account or a product that stopped being usable looks like.
 */
export function validateOrderDraft(
  values: OrderDraftFormValues,
  options: OrderDraftOptions,
): OrderDraftValidation {
  const text = ORDER_DRAFT_TEXT.validation
  const errors: Record<string, string> = {}

  if (values.partyId === '' || values.companyAccountId === '') {
    errors[ORDER_FIELD.customer] = text.customerRequired
  } else if (
    !options.accounts.some(
      (account) =>
        account.partyId === values.partyId && account.companyAccountId === values.companyAccountId,
    )
  ) {
    errors[ORDER_FIELD.customer] = text.customerNotOffered
  }

  if (values.businessDate === '') errors[ORDER_FIELD.businessDate] = text.businessDateRequired
  else if (!isDateOnly(values.businessDate)) errors[ORDER_FIELD.businessDate] = text.dateInvalid

  if (values.currencyId === '') errors[ORDER_FIELD.currency] = text.currencyRequired
  else if (!options.currencyIds.includes(values.currencyId)) {
    errors[ORDER_FIELD.currency] = text.currencyNotOffered
  }

  if (values.lines.length === 0) errors[ORDER_FIELD.lines] = text.linesRequired

  const lines: OrderDraftLineRequest[] = []
  for (const line of values.lines) {
    const productOffered = options.productIds.includes(line.productId)
    if (line.productId === '') errors[lineFieldId(line.key, 'productId')] = text.productRequired
    else if (!productOffered) errors[lineFieldId(line.key, 'productId')] = text.productNotOffered

    const quantity = parseQuantityInput(line.quantityText)
    if (!quantity.ok) errors[lineFieldId(line.key, 'quantity')] = QUANTITY_MESSAGE[quantity.error]

    if (line.orderUnitId === '') {
      errors[lineFieldId(line.key, 'orderUnitId')] = text.orderUnitRequired
    } else if (
      // Judged only against a product that is itself offered; otherwise the product
      // error already says what is wrong and a second message would only add noise.
      productOffered &&
      !(options.unitIdsByProduct[line.productId] ?? []).includes(line.orderUnitId)
    ) {
      errors[lineFieldId(line.key, 'orderUnitId')] = text.orderUnitNotOffered
    }

    if (line.requestedDeliveryDate !== '' && !isDateOnly(line.requestedDeliveryDate)) {
      errors[lineFieldId(line.key, 'requestedDeliveryDate')] = text.dateInvalid
    }

    if (quantity.ok) {
      lines.push({
        productId: line.productId,
        quantity: quantity.value,
        orderUnitId: line.orderUnitId,
        requestedDeliveryDate: line.requestedDeliveryDate === '' ? null : line.requestedDeliveryDate,
      })
    }
  }

  if (Object.keys(errors).length > 0) return { ok: false, errors }

  return {
    ok: true,
    payload: {
      kind: ORDER_KIND,
      partyId: values.partyId,
      companyAccountId: values.companyAccountId,
      businessDate: values.businessDate,
      currencyId: values.currencyId,
      lines,
    },
  }
}

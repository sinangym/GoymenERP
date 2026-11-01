/**
 * Sales order draft form: values, validation and the one normalized create body.
 *
 * Pinned to #257 @ 2c1239e (`CreateSalesOrderDraftRequest`) on #254 @ 6f0cd10. Wire
 * names live in ./contract.ts.
 *
 * THE RULES THIS FORM ENCODES, and where each comes from:
 *  - every reference (Cari account, currency, product + its version, order unit) is
 *    CHOSEN from what an owner offers and never typed. Not offered is refused exactly
 *    like invented: a passive account, another company's account, a product the
 *    Product owner did not report usable, a unit the product does not offer;
 *  - `lineId` and `sliceId` are client-owned UUIDs minted ONCE when the row is added
 *    and kept for the life of the form, so a retry sends the same identities (§Create);
 *  - the company is not a form value: it is the committed shell context, sent as
 *    request context, never in the body;
 *  - quantities stay text end to end; an empty date is null — unknown — never today;
 *  - UI-14 (on TEK-03): a line's unit price, its price unit and its discount percent
 *    are offered ONLY to an actor the shell publishes the price-write control to
 *    (`sales.orders.price.write`); without it they go out as null (unknown) whatever
 *    the form holds. The price unit is CHOSEN from the units the Product owner offers
 *    for the line's product. The client judges the text's shape only (the quantity's
 *    separator rule; zero is an explicit price) and the pairings the wire requires
 *    (price ↔ price unit, no discount without a price); the scale, the discount range
 *    and every amount are the server's (money profile, SAT-D11) — no total or net is
 *    computed here. Empty is null, never zero;
 *  - SALES-A2a: the delivery address and the contact person are CHOSEN from the Cari
 *    owner's contact information of the chosen account (id + the owner's version) and
 *    are optional — nothing chosen is null (unknown), never a default address;
 *  - the other four owner references (responsible, payment terms, source, package) go
 *    out as null: no authorized read exists and no policy was decided for them;
 *  - SAT-D123 / SAT-D127: the due terms ("vade") are optional at create and typed by hand
 *    (never defaulted): none is null ("vade yok"); otherwise one or more instalments, each
 *    with its own due type (a mixed "karma" plan is one list), days and share. The shape is
 *    checked here; whether the shares add up to 100 is the server's rule.
 */

import {
  LIMITS,
  ORDER_KIND,
  type AddressOption,
  type ContactOption,
  type DeliverySlice,
  type DraftCreateRequest,
  type DraftLineRequest,
  type OwnerReference,
} from './contract'
import { unplannedQuantity } from './deliveryPlan'
import { buildDueTerms, type DueTermsFormError, type InstalmentRow } from './headerRevision'
import { HEADER_REVISION_TEXT, ORDER_DRAFT_TEXT } from './salesText'
import { isDateOnly, parseDecimalInput, parseQuantityInput, type DecimalInputError, type QuantityInputError } from './wirePrimitives'

/** What the owners currently offer. Filled only from a real selection source. */
export interface OrderDraftOptions {
  /** Active Party + an active account of the COMMITTED company, offered as a pair. */
  readonly accounts: readonly {
    readonly partyId: string
    readonly companyAccountId: string
    readonly label: string
  }[]
  readonly currencies: readonly { readonly currencyId: string; readonly label: string }[]
  /** Products the Product owner reports usable for sales, at a specific version. */
  readonly products: readonly {
    readonly productId: string
    readonly productVersion: string
    readonly code: string
    readonly name: string
    readonly label: string
    /** Order units the owner offers for this product. Never assumed to be kg. */
    readonly orderUnits: readonly {
      readonly unitId: string
      readonly code: string
      readonly label: string
      readonly profileId: string
      readonly profileVersion: string
    }[]
  }[]
}

export interface SliceValues {
  /** The wire `sliceId`, minted when the slice was added. */
  readonly sliceId: string
  readonly quantityText: string
  readonly requestedDeliveryDate: string
}

export interface LineValues {
  /** The wire `lineId`, minted when the line was added. Also the React key and field-id stem. */
  readonly lineId: string
  readonly productId: string
  readonly quantityText: string
  readonly orderUnitId: string
  readonly requestedDeliveryDate: string
  readonly note: string
  /**
   * null until the person plans a first slice: an unknown plan. Removing the last
   * slice returns to null rather than sending `[]` ("no planned slices"), because a
   * person who deleted their rows has not stated that nothing is planned.
   */
  readonly slices: readonly SliceValues[] | null
  /** UI-14: the unit price as typed ('' = no price → null on the wire, never zero). */
  readonly unitPriceText: string
  /** UI-14: one of the product's offered units, or '' (nothing chosen). */
  readonly priceUnitId: string
  /** UI-14: the discount percent as typed ('' = null). */
  readonly discountPercentText: string
}

export interface OrderDraftFormValues {
  /** Both empty, or both from one offered account entry. */
  readonly partyId: string
  readonly companyAccountId: string
  readonly businessDate: string
  readonly currencyId: string
  readonly requestedDeliveryDate: string
  readonly note: string
  readonly lines: readonly LineValues[]
  /** SALES-A2a: an address / contact of the CHOSEN account, or '' (nothing chosen → null on the wire). */
  readonly deliveryAddressId: string
  readonly contactId: string
  /** SAT-D123: null = no due terms ("vade yok"); otherwise the editor's rows as typed. */
  readonly instalments: readonly InstalmentRow[] | null
}

/** SALES-A2a: what the Cari owner lists for the chosen account; null while nothing is offered (not read, or failed). */
export interface DeliveryReferenceOptions {
  readonly addresses: readonly AddressOption[]
  readonly contacts: readonly ContactOption[]
}

export type LineField =
  | 'productId'
  | 'quantity'
  | 'orderUnitId'
  | 'requestedDeliveryDate'
  | 'note'
  | 'slices'
  | 'unitPrice'
  | 'priceUnitId'
  | 'discountPercent'
export type SliceField = 'quantity' | 'requestedDeliveryDate'

export const ORDER_FIELD = {
  customer: 'customer',
  businessDate: 'businessDate',
  currency: 'currency',
  requestedDeliveryDate: 'requestedDeliveryDate',
  note: 'note',
  lines: 'lines',
  deliveryAddress: 'deliveryAddress',
  contact: 'contact',
  dueTerms: 'dueTerms',
} as const

export const lineFieldId = (lineId: string, field: LineField) => `lines.${lineId}.${field}`
export const sliceFieldId = (lineId: string, sliceId: string, field: SliceField) =>
  `lines.${lineId}.slices.${sliceId}.${field}`

export type OrderDraftErrors = Readonly<Record<string, string>>

export type OrderDraftValidation =
  | { readonly ok: true; readonly payload: DraftCreateRequest }
  | { readonly ok: false; readonly errors: OrderDraftErrors }

/** Mints a UUID. Injected so tests can prove an identity was minted once. */
export type IdSource = () => string

/**
 * A random (v4) UUID from `getRandomValues`. Not `crypto.randomUUID`: that exists only
 * in a secure context, and a plain-HTTP intranet host would throw at "add line".
 */
export const browserIds: IdSource = () => {
  const bytes = new Uint8Array(16)
  globalThis.crypto.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

export function emptyLine(lineId: string): LineValues {
  return {
    lineId,
    productId: '',
    quantityText: '',
    orderUnitId: '',
    requestedDeliveryDate: '',
    note: '',
    slices: null,
    unitPriceText: '',
    priceUnitId: '',
    discountPercentText: '',
  }
}

/**
 * The starting form: one blank line. The business date is blank on purpose — the
 * browser's "today" is not necessarily the company's business day, and the contract
 * says "no default today".
 */
export function emptyOrderDraftForm(firstLineId: string): OrderDraftFormValues {
  return {
    partyId: '',
    companyAccountId: '',
    businessDate: '',
    currencyId: '',
    requestedDeliveryDate: '',
    note: '',
    lines: [emptyLine(firstLineId)],
    deliveryAddressId: '',
    contactId: '',
    instalments: null,
  }
}

export function isOrderDraftPristine(values: OrderDraftFormValues): boolean {
  return (
    values.partyId === '' &&
    values.companyAccountId === '' &&
    values.businessDate === '' &&
    values.currencyId === '' &&
    values.requestedDeliveryDate === '' &&
    values.note.trim() === '' &&
    values.deliveryAddressId === '' &&
    values.contactId === '' &&
    values.instalments === null &&
    values.lines.every(
      (line) =>
        line.productId === '' &&
        line.quantityText.trim() === '' &&
        line.orderUnitId === '' &&
        line.requestedDeliveryDate === '' &&
        line.note.trim() === '' &&
        line.slices === null &&
        line.unitPriceText.trim() === '' &&
        line.priceUnitId === '' &&
        line.discountPercentText.trim() === '',
    )
  )
}

const mapLine = (values: OrderDraftFormValues, lineId: string, change: (line: LineValues) => LineValues) => ({
  ...values,
  lines: values.lines.map((line) => (line.lineId === lineId ? change(line) : line)),
})

export const addLine = (values: OrderDraftFormValues, lineId: string): OrderDraftFormValues => ({
  ...values,
  lines: [...values.lines, emptyLine(lineId)],
})

export const removeLine = (values: OrderDraftFormValues, lineId: string): OrderDraftFormValues => ({
  ...values,
  lines: values.lines.filter((line) => line.lineId !== lineId),
})

export const updateLine = (
  values: OrderDraftFormValues,
  lineId: string,
  patch: Partial<Omit<LineValues, 'lineId' | 'slices' | 'productId'>>,
): OrderDraftFormValues => mapLine(values, lineId, (line) => ({ ...line, ...patch }))

/**
 * Choosing another product clears the line's unit. Units are offered PER product; a
 * unit chosen for the previous one riding onto the new one is a reference nobody
 * offered for it.
 */
/**
 * SALES-A2a: choosing another account clears the delivery address and contact. They are offered PER
 * account; a reference chosen for the previous account riding onto the new one is one nobody offered.
 */
export const setAccount = (values: OrderDraftFormValues, partyId: string, companyAccountId: string): OrderDraftFormValues =>
  values.partyId === partyId && values.companyAccountId === companyAccountId
    ? values
    : { ...values, partyId, companyAccountId, deliveryAddressId: '', contactId: '' }

export const setLineProduct = (values: OrderDraftFormValues, lineId: string, productId: string) =>
  mapLine(values, lineId, (line) =>
    line.productId === productId ? line : { ...line, productId, orderUnitId: '', priceUnitId: '' },
  )

export const addSlice = (values: OrderDraftFormValues, lineId: string, sliceId: string) =>
  mapLine(values, lineId, (line) => ({
    ...line,
    slices: [...(line.slices ?? []), { sliceId, quantityText: '', requestedDeliveryDate: '' }],
  }))

export const updateSlice = (
  values: OrderDraftFormValues,
  lineId: string,
  sliceId: string,
  patch: Partial<Omit<SliceValues, 'sliceId'>>,
) =>
  mapLine(values, lineId, (line) => ({
    ...line,
    slices: (line.slices ?? []).map((slice) => (slice.sliceId === sliceId ? { ...slice, ...patch } : slice)),
  }))

export const removeSlice = (values: OrderDraftFormValues, lineId: string, sliceId: string) =>
  mapLine(values, lineId, (line) => {
    const rest = (line.slices ?? []).filter((slice) => slice.sliceId !== sliceId)
    return { ...line, slices: rest.length === 0 ? null : rest }
  })

const QUANTITY_MESSAGE: Record<QuantityInputError, string> = {
  required: ORDER_DRAFT_TEXT.validation.quantityRequired,
  invalid: ORDER_DRAFT_TEXT.validation.quantityInvalid,
  'ambiguous-separator': ORDER_DRAFT_TEXT.validation.quantityAmbiguous,
  'not-positive': ORDER_DRAFT_TEXT.validation.quantityNotPositive,
}

const noteOrNull = (text: string) => (text.trim() === '' ? null : text)

const PRICE_MESSAGE: Record<Exclude<DecimalInputError, 'required'>, string> = {
  invalid: ORDER_DRAFT_TEXT.validation.priceInvalid,
  'ambiguous-separator': ORDER_DRAFT_TEXT.validation.priceAmbiguous,
}

const DISCOUNT_MESSAGE: Record<Exclude<DecimalInputError, 'required'>, string> = {
  invalid: ORDER_DRAFT_TEXT.validation.discountInvalid,
  'ambiguous-separator': ORDER_DRAFT_TEXT.validation.discountAmbiguous,
}

/** An optional decimal field: '' is null (unknown), else the canonical text or the shape's error. */
function optionalDecimal(raw: string): { readonly ok: true; readonly value: string | null } | { readonly ok: false; readonly error: Exclude<DecimalInputError, 'required'> } {
  if (raw.trim() === '') return { ok: true, value: null }
  const parsed = parseDecimalInput(raw)
  if (parsed.ok) return parsed
  return { ok: false, error: parsed.error === 'required' ? 'invalid' : parsed.error }
}

interface LinePrice {
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly discountPercent: string | null
}

const NO_PRICE: LinePrice = { unitPrice: null, priceUnitId: null, discountPercent: null }

/**
 * UI-14: a line's price fields as the wire takes them, or the field errors. Shape and pairing only; the server decides
 * the scale, the unit's binding and the discount's range, and refuses with a field code (mapped back to the field).
 */
function linePrice(
  line: LineValues,
  product: OrderDraftOptions['products'][number] | undefined,
  errors: Record<string, string>,
): LinePrice {
  const text = ORDER_DRAFT_TEXT.validation
  const price = optionalDecimal(line.unitPriceText)
  const discount = optionalDecimal(line.discountPercentText)
  const before = Object.keys(errors).length
  if (!price.ok) errors[lineFieldId(line.lineId, 'unitPrice')] = PRICE_MESSAGE[price.error]
  const priced = !price.ok || price.value !== null
  if (line.priceUnitId === '') {
    if (priced) errors[lineFieldId(line.lineId, 'priceUnitId')] = text.priceUnitRequired
  } else if (!priced) {
    errors[lineFieldId(line.lineId, 'priceUnitId')] = text.priceUnitWithoutPrice
  } else if (product !== undefined && !product.orderUnits.some((unit) => unit.unitId === line.priceUnitId)) {
    errors[lineFieldId(line.lineId, 'priceUnitId')] = text.priceUnitNotOffered
  }
  if (!discount.ok) errors[lineFieldId(line.lineId, 'discountPercent')] = DISCOUNT_MESSAGE[discount.error]
  else if (discount.value !== null && !priced) errors[lineFieldId(line.lineId, 'discountPercent')] = text.discountWithoutPrice
  if (!price.ok || !discount.ok || Object.keys(errors).length !== before) return NO_PRICE
  return {
    unitPrice: price.value,
    priceUnitId: price.value === null ? null : line.priceUnitId,
    discountPercent: discount.value,
  }
}

/** A line's plan as the person can currently read it, for the "still unplanned" hint. */
export function linePlanHint(line: LineValues): { readonly unplanned: string } | { readonly exceeds: true } | null {
  const quantity = parseQuantityInput(line.quantityText)
  if (!quantity.ok || line.slices === null) return null
  const slices: { orderUnitId: string; quantity: string }[] = []
  for (const slice of line.slices) {
    const value = parseQuantityInput(slice.quantityText)
    if (!value.ok) return null
    slices.push({ orderUnitId: 'line', quantity: value.value })
  }
  const result = unplannedQuantity({ orderUnitId: 'line', quantity: quantity.value }, slices)
  if (result.ok) return { unplanned: result.unplanned }
  return result.error === 'exceeds-order' ? { exceeds: true } : null
}

/**
 * Validate and normalize. Errors are keyed in DOM order: header first, then each
 * line's fields and its slices in the order they are rendered.
 */
export function validateOrderDraft(
  values: OrderDraftFormValues,
  options: OrderDraftOptions,
  delivery: DeliveryReferenceOptions | null = null,
  /** UI-14: the shell published the price-write control; without it no price member is read or sent. */
  priceWrite = false,
): OrderDraftValidation {
  const text = ORDER_DRAFT_TEXT.validation
  const errors: Record<string, string> = {}

  if (values.partyId === '' || values.companyAccountId === '') {
    errors[ORDER_FIELD.customer] = text.customerRequired
  } else if (
    !options.accounts.some(
      (a) => a.partyId === values.partyId && a.companyAccountId === values.companyAccountId,
    )
  ) {
    errors[ORDER_FIELD.customer] = text.customerNotOffered
  }

  if (values.businessDate === '') errors[ORDER_FIELD.businessDate] = text.businessDateRequired
  else if (!isDateOnly(values.businessDate)) errors[ORDER_FIELD.businessDate] = text.dateInvalid

  if (values.currencyId === '') errors[ORDER_FIELD.currency] = text.currencyRequired
  else if (!options.currencies.some((c) => c.currencyId === values.currencyId)) {
    errors[ORDER_FIELD.currency] = text.currencyNotOffered
  }

  if (values.requestedDeliveryDate !== '' && !isDateOnly(values.requestedDeliveryDate)) {
    errors[ORDER_FIELD.requestedDeliveryDate] = text.dateInvalid
  }
  if (values.note.length > LIMITS.note) errors[ORDER_FIELD.note] = text.noteTooLong

  // SALES-A2a: optional, but a chosen one must be what the owner lists for the chosen account, at the
  // owner's version — a reference the owner no longer offers is refused like an invented one.
  const address = delivery?.addresses.find((a) => a.id === values.deliveryAddressId)
  const contact = delivery?.contacts.find((c) => c.id === values.contactId)
  if (values.deliveryAddressId !== '' && address === undefined) errors[ORDER_FIELD.deliveryAddress] = text.deliveryAddressNotOffered
  if (values.contactId !== '' && contact === undefined) errors[ORDER_FIELD.contact] = text.contactNotOffered
  const reference = (option: { readonly id: string; readonly version: string } | undefined): OwnerReference | null =>
    option === undefined ? null : { id: option.id, expectedVersion: option.version }

  // SAT-D123: the due terms follow the delivery references in DOM order.
  const terms = buildDueTerms(values.instalments)
  if (!terms.ok) errors[ORDER_FIELD.dueTerms] = dueTermsMessage(terms.error)

  if (values.lines.length === 0) errors[ORDER_FIELD.lines] = text.linesRequired
  else if (values.lines.length > LIMITS.lines) errors[ORDER_FIELD.lines] = text.tooManyLines

  const lines: DraftLineRequest[] = []
  for (const line of values.lines) {
    const product = options.products.find((p) => p.productId === line.productId)
    if (line.productId === '') errors[lineFieldId(line.lineId, 'productId')] = text.productRequired
    else if (product === undefined) errors[lineFieldId(line.lineId, 'productId')] = text.productNotOffered

    const quantity = parseQuantityInput(line.quantityText)
    if (!quantity.ok) errors[lineFieldId(line.lineId, 'quantity')] = QUANTITY_MESSAGE[quantity.error]

    if (line.orderUnitId === '') {
      errors[lineFieldId(line.lineId, 'orderUnitId')] = text.orderUnitRequired
    } else if (product !== undefined && !product.orderUnits.some((u) => u.unitId === line.orderUnitId)) {
      // Judged only against an offered product; otherwise the product error already says it.
      errors[lineFieldId(line.lineId, 'orderUnitId')] = text.orderUnitNotOffered
    }

    // DOM order: the price group follows the line's main fields and precedes the optional details.
    const price = priceWrite ? linePrice(line, product, errors) : NO_PRICE

    if (line.requestedDeliveryDate !== '' && !isDateOnly(line.requestedDeliveryDate)) {
      errors[lineFieldId(line.lineId, 'requestedDeliveryDate')] = text.dateInvalid
    }
    if (line.note.length > LIMITS.note) errors[lineFieldId(line.lineId, 'note')] = text.noteTooLong

    let slices: DeliverySlice[] | null = null
    if (line.slices !== null) {
      slices = []
      if (line.slices.length > LIMITS.slices) errors[lineFieldId(line.lineId, 'slices')] = text.tooManySlices
      for (const slice of line.slices) {
        const sliceQuantity = parseQuantityInput(slice.quantityText)
        if (!sliceQuantity.ok) {
          errors[sliceFieldId(line.lineId, slice.sliceId, 'quantity')] = QUANTITY_MESSAGE[sliceQuantity.error]
        }
        // A slice IS a dated quantity: unlike the line's own due date it cannot be unknown.
        if (slice.requestedDeliveryDate === '') {
          errors[sliceFieldId(line.lineId, slice.sliceId, 'requestedDeliveryDate')] = text.sliceDateRequired
        } else if (!isDateOnly(slice.requestedDeliveryDate)) {
          errors[sliceFieldId(line.lineId, slice.sliceId, 'requestedDeliveryDate')] = text.dateInvalid
        }
        if (sliceQuantity.ok) {
          slices.push({
            sliceId: slice.sliceId,
            orderUnitId: line.orderUnitId,
            quantity: sliceQuantity.value,
            requestedDeliveryDate: slice.requestedDeliveryDate,
          })
        }
      }
      if (quantity.ok && slices.length === line.slices.length) {
        const plan = unplannedQuantity({ orderUnitId: line.orderUnitId, quantity: quantity.value }, slices)
        if (!plan.ok && plan.error === 'exceeds-order') {
          errors[lineFieldId(line.lineId, 'slices')] = text.slicesExceedOrder
        }
      }
    }

    if (quantity.ok && product !== undefined) {
      lines.push({
        lineId: line.lineId,
        productId: line.productId,
        expectedProductVersion: product.productVersion,
        orderUnitId: line.orderUnitId,
        quantity: quantity.value,
        unitPrice: price.unitPrice,
        priceUnitId: price.priceUnitId,
        requestedDeliveryDate: line.requestedDeliveryDate === '' ? null : line.requestedDeliveryDate,
        note: noteOrNull(line.note),
        requestedDeliverySlices: slices,
        discountPercent: price.discountPercent,
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
      requestedDeliveryDate: values.requestedDeliveryDate === '' ? null : values.requestedDeliveryDate,
      note: noteOrNull(values.note),
      deliveryAddress: reference(address),
      contact: reference(contact),
      responsible: null,
      paymentTerms: null,
      sourceStrategy: null,
      package: null,
      dueTerms: terms.ok ? terms.dueTerms : null,
    },
  }
}

/** The create form's text for a due-terms row error (the header revision's own wording). */
export function dueTermsMessage(error: DueTermsFormError): string {
  return HEADER_REVISION_TEXT.localError(error)
}

/**
 * Where a server field error belongs on this form.
 *
 * The server names fields by wire path (`lines[0].productId`, `companyAccountId`). The
 * index is resolved against the SENT snapshot, not the form as it is now — the person
 * may have added or removed rows since. A path this form does not render returns null
 * and the caller shows the message in the summary, so nothing goes silent.
 */
export function formFieldForServerPath(path: string, sentLines: readonly { readonly lineId: string }[]): string | null {
  if (path === 'partyId' || path === 'companyAccountId') return ORDER_FIELD.customer
  if (path === 'currencyId') return ORDER_FIELD.currency
  if (path === 'businessDate') return ORDER_FIELD.businessDate
  if (path === 'requestedDeliveryDate') return ORDER_FIELD.requestedDeliveryDate
  if (path === 'note') return ORDER_FIELD.note
  if (path === 'lines') return ORDER_FIELD.lines
  if (path === 'deliveryAddress') return ORDER_FIELD.deliveryAddress
  if (path === 'contact') return ORDER_FIELD.contact
  if (path === 'dueTerms' || path.startsWith('dueTerms.') || path.startsWith('dueTerms[')) return ORDER_FIELD.dueTerms
  const match =
    /^lines\[(\d+)\]\.(productId|expectedProductVersion|orderUnitId|quantity|requestedDeliveryDate|note|requestedDeliverySlices|unitPrice|priceUnitId|discountPercent)/.exec(
      path,
    )
  if (match === null) return null
  const line = sentLines[Number(match[1])]
  if (line === undefined) return null
  const member = match[2]
  const field: LineField =
    member === 'expectedProductVersion'
      ? 'productId'
      : member === 'requestedDeliverySlices'
        ? 'slices'
        : (member as LineField)
  return lineFieldId(line.lineId, field)
}

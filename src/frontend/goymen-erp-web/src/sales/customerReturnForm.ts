/**
 * UI-70b S-10 — the customer return create form's values, its own shape checks and the mapping of the server's field
 * errors. Every business limit (remaining quantity, unshipped balance, invoice state, duplicate document) is the server's.
 */
import type { CreateCustomerReturnBody } from './customerReturnApi'
import { CUSTOMER_RETURN_DOCUMENT_NO_MAX, CUSTOMER_RETURN_MAX_LINES } from './customerReturnContract'
import { CUSTOMER_RETURN_TEXT, customerReturnProblemText } from './customerReturnText'
import type { InvoiceLine, SalesInvoiceDetail } from './invoiceFinalizationContract'
import { POOL_TEXT } from './poolText'
import { isDateOnly, parseQuantityInput } from './wirePrimitives'

const T = CUSTOMER_RETURN_TEXT.create

/** The form's own field keys (the server names the same ones in its field errors). */
export const CUSTOMER_RETURN_FIELD = {
  invoice: 'originalInvoiceId',
  documentNo: 'customerDocumentNo',
  documentDate: 'customerDocumentDate',
  shipment: 'shipmentId',
  lines: 'lines',
  line: (lineId: string) => `line.${lineId}`,
} as const

export interface CustomerReturnFormValues {
  readonly invoiceId: string
  readonly documentNo: string
  readonly documentDate: string
  /** '' when the return names no shipment. */
  readonly shipmentId: string
  /** The typed quantity per original invoice line id ('' = not returned). */
  readonly quantities: Readonly<Record<string, string>>
}

export type CustomerReturnFormErrors = Readonly<Record<string, string>>

export interface ShipmentOption {
  readonly shipmentId: string
  readonly dispatchNoteNumber: string
}

export function shipmentsOf(detail: SalesInvoiceDetail): readonly ShipmentOption[] {
  const seen = new Map<string, ShipmentOption>()
  for (const line of detail.lines) {
    if (!seen.has(line.shipmentId)) seen.set(line.shipmentId, { shipmentId: line.shipmentId, dispatchNoteNumber: line.dispatchNoteNumber })
  }
  return [...seen.values()]
}

/** The invoice lines a return may name: every line, or only the chosen shipment's lines (SAT-D293). */
export function visibleLines(detail: SalesInvoiceDetail, shipmentId: string): readonly InvoiceLine[] {
  return shipmentId === '' ? detail.lines : detail.lines.filter((line) => line.shipmentId === shipmentId)
}

type Validation = { readonly ok: true; readonly payload: CreateCustomerReturnBody } | { readonly ok: false; readonly errors: CustomerReturnFormErrors }

/**
 * The form's own shape checks (what the request contract itself requires). Every business limit — the remaining
 * quantity, the unshipped balance, the invoice's state, a duplicate document — is the server's, and its refusal is shown.
 */
export function validateCustomerReturnForm(values: CustomerReturnFormValues, detail: SalesInvoiceDetail | null): Validation {
  const errors: Record<string, string> = {}
  if (values.invoiceId === '') errors[CUSTOMER_RETURN_FIELD.invoice] = T.invoiceRequired
  const documentNo = values.documentNo.trim()
  if (documentNo === '') errors[CUSTOMER_RETURN_FIELD.documentNo] = T.documentNoRequired
  else if (documentNo.length > CUSTOMER_RETURN_DOCUMENT_NO_MAX) errors[CUSTOMER_RETURN_FIELD.documentNo] = T.documentNoTooLong(CUSTOMER_RETURN_DOCUMENT_NO_MAX)
  if (!isDateOnly(values.documentDate)) errors[CUSTOMER_RETURN_FIELD.documentDate] = T.documentDateRequired

  const lines: { originalInvoiceLineId: string; quantity: string }[] = []
  if (detail !== null && detail.id.toLowerCase() === values.invoiceId.toLowerCase()) {
    for (const line of visibleLines(detail, values.shipmentId)) {
      const raw = (values.quantities[line.lineId] ?? '').trim()
      if (raw === '') continue
      const parsed = parseQuantityInput(raw)
      if (!parsed.ok) errors[CUSTOMER_RETURN_FIELD.line(line.lineId)] = T.quantityInvalid
      else lines.push({ originalInvoiceLineId: line.lineId, quantity: parsed.value })
    }
    const anyLineError = Object.keys(errors).some((key) => key.startsWith('line.'))
    if (lines.length === 0 && !anyLineError) errors[CUSTOMER_RETURN_FIELD.lines] = T.linesRequired
    else if (lines.length > CUSTOMER_RETURN_MAX_LINES) errors[CUSTOMER_RETURN_FIELD.lines] = T.tooManyLines(CUSTOMER_RETURN_MAX_LINES)
  } else if (values.invoiceId !== '') {
    errors[CUSTOMER_RETURN_FIELD.lines] = T.linesRequired
  }

  if (Object.keys(errors).length > 0) return { ok: false, errors }
  return {
    ok: true,
    payload: {
      originalInvoiceId: values.invoiceId,
      customerDocumentNo: documentNo,
      customerDocumentDate: values.documentDate,
      shipmentId: values.shipmentId === '' ? null : values.shipmentId,
      lines,
    },
  }
}

const SERVER_FIELDS: ReadonlySet<string> = new Set([
  CUSTOMER_RETURN_FIELD.invoice,
  CUSTOMER_RETURN_FIELD.documentNo,
  CUSTOMER_RETURN_FIELD.documentDate,
  CUSTOMER_RETURN_FIELD.shipment,
  CUSTOMER_RETURN_FIELD.lines,
])

/** The server's field errors on the fields they name; a field this form does not render is listed under "lines". */
export function serverCustomerReturnErrors(entries: readonly { readonly field: string; readonly code: string }[]): CustomerReturnFormErrors {
  const errors: Record<string, string> = {}
  for (const entry of entries) {
    const field = SERVER_FIELDS.has(entry.field) ? entry.field : CUSTOMER_RETURN_FIELD.lines
    errors[field] ??= customerReturnProblemText(entry.code) ?? POOL_TEXT.form.serverFieldError
  }
  return errors
}

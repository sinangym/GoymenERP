/**
 * UI-63 S-09 "Satış faturaları": the company's Sales invoice list, `GET /api/v1/sales/invoices?pageSize&cursor`
 * (API-32, `SalesInvoiceListView` of `SalesInvoiceSummaryView`; `sales.invoices.read` only — SAT-D238, Sales shell 1.20).
 *
 * SAT-D238 (EK48): `sales.orders.price.read` is asked, never required. Every row carries `amountDisclosure`: `Visible` → the
 * payable amount is the server's text (and the TL text for a foreign-currency invoice, otherwise null); `Withheld` → BOTH amounts
 * are null because the caller may not see them. A withheld amount is never a zero; any other combination is not this contract.
 *
 * The server orders the list (newest first) and offers no filter and no sort, so the screen sends only `pageSize` and the
 * owner's `cursor`. Strict and fail-closed: every object carries exactly the members `contracts/openapi/goymen-erp-v1.json`
 * declares, the counts are JSON numbers, every amount stays the server's TEXT, and anything outside these shapes makes the
 * whole page `null` ("Beklenmeyen yanıt") — never a partial list.
 */
import { INVOICE_STATES, type InvoiceState } from './invoiceFinalizationContract'
import { isDateOnly, isReferenceId, isVersionString } from './wirePrimitives'

export const SALES_INVOICES_PATH = '/sales/invoices'
/** The server's default page (`SalesInvoiceApiV1.DefaultPageSize`), sent explicitly so the request is the one tested. */
export const SALES_INVOICE_PAGE_SIZE = 50

export function salesInvoicesListPath(cursor: string | null): string {
  const params = new URLSearchParams({ pageSize: String(SALES_INVOICE_PAGE_SIZE) })
  if (cursor !== null) params.set('cursor', cursor)
  return `${SALES_INVOICES_PATH}?${params.toString()}`
}

/** SAT-D238: whether the row's amounts are disclosed to this caller (`SalesPriceDisclosure`). */
export type InvoiceAmountDisclosure = 'Visible' | 'Withheld'

export interface SalesInvoiceSummary {
  readonly id: string
  readonly documentNo: string
  readonly state: InvoiceState
  readonly version: string
  readonly businessDate: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly amountDisclosure: InvoiceAmountDisclosure
  /** Null only when `amountDisclosure` is `Withheld`. */
  readonly payableAmount: string | null
  readonly functionalPayableAmount: string | null
  readonly lineCount: number
  readonly createdAtUtc: string
}

export interface SalesInvoicePage {
  readonly items: readonly SalesInvoiceSummary[]
  readonly nextCursor: string | null
}

type Obj = Record<string, unknown>

function exact(value: unknown, fields: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const object = value as Obj
  const keys = Object.keys(object)
  if (keys.length !== fields.length) return null
  return fields.every((field) => Object.hasOwn(object, field)) ? object : null
}

const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
const AMOUNT_RE = /^\d+(?:\.\d+)?$/
const TEXT_MAX = 512
const CURSOR_MAX = 8192

const isInstant = (v: unknown): v is string =>
  typeof v === 'string' && v.length <= 64 && INSTANT_RE.test(v) && !Number.isNaN(Date.parse(v))
const isAmount = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && AMOUNT_RE.test(v)
const isText = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= TEXT_MAX
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0
const isState = (v: unknown): v is InvoiceState => typeof v === 'string' && (INVOICE_STATES as readonly string[]).includes(v)
const isCursor = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= CURSOR_MAX

const SUMMARY_KEYS = [
  'id',
  'documentNo',
  'state',
  'version',
  'businessDate',
  'partyId',
  'companyAccountId',
  'currencyId',
  'amountDisclosure',
  'payableAmount',
  'functionalPayableAmount',
  'lineCount',
  'createdAtUtc',
] as const

/** Visible → an amount (and a null-or-amount TL text); Withheld → both null. Anything else fails the whole page. */
function amountsAgreeWithDisclosure(disclosure: unknown, payable: unknown, functional: unknown): boolean {
  if (disclosure === 'Visible') return isAmount(payable) && (functional === null || isAmount(functional))
  if (disclosure === 'Withheld') return payable === null && functional === null
  return false
}

function decodeSummary(value: unknown): SalesInvoiceSummary | null {
  const o = exact(value, SUMMARY_KEYS)
  if (o === null) return null
  if (
    !isReferenceId(o.id)
    || !isText(o.documentNo)
    || !isState(o.state)
    || !isVersionString(o.version)
    || !isDateOnly(o.businessDate)
    || !isReferenceId(o.partyId)
    || !isReferenceId(o.companyAccountId)
    || !isReferenceId(o.currencyId)
    || !amountsAgreeWithDisclosure(o.amountDisclosure, o.payableAmount, o.functionalPayableAmount)
    || !isCount(o.lineCount)
    || !isInstant(o.createdAtUtc)
  ) {
    return null
  }
  return {
    id: o.id,
    documentNo: o.documentNo,
    state: o.state,
    version: o.version,
    businessDate: o.businessDate,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    currencyId: o.currencyId,
    amountDisclosure: o.amountDisclosure as InvoiceAmountDisclosure,
    payableAmount: o.payableAmount as string | null,
    functionalPayableAmount: o.functionalPayableAmount as string | null,
    lineCount: o.lineCount,
    createdAtUtc: o.createdAtUtc,
  }
}

/** The page as the server wrote it; a repeated id inside one page is not this contract. */
export function decodeSalesInvoicePage(value: unknown): SalesInvoicePage | null {
  const o = exact(value, ['items', 'nextCursor'])
  if (o === null || !Array.isArray(o.items) || !(o.nextCursor === null || isCursor(o.nextCursor))) return null
  const items: SalesInvoiceSummary[] = []
  const seen = new Set<string>()
  for (const entry of o.items) {
    const item = decodeSummary(entry)
    if (item === null || seen.has(item.id)) return null
    seen.add(item.id)
    items.push(item)
  }
  return { items, nextCursor: o.nextCursor }
}

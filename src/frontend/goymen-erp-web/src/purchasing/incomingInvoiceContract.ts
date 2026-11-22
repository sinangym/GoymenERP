import { decodePurchaseKgPrice, isPurchasingGuid as guid, isPurchasingVersion as version, type PurchaseKgPrice } from './contract'
import { isDateOnly } from '../finance/dateOnly'

export interface InvoiceAmounts { readonly net: string; readonly vat: string; readonly withholding: string; readonly payable: string }
export interface IncomingInvoiceLine {
  readonly id: string; readonly lineNo: number; readonly productId: string; readonly unitId: string; readonly quantity: string
  readonly itemType: string; readonly linePurpose: string; readonly vatPercent: string
  readonly amounts: InvoiceAmounts; readonly functionalAmounts: InvoiceAmounts | null
  readonly serviceTypeId?: string
  readonly expenseTypeId?: string
  /**
   * AK-3b (SAT-D436, SAT-D439): the line's kg price indicator in the INVOICE's currency, derived by the server from the invoice ↔
   * receipt match; absent while the indicator is off and on a service or expense line. Information only, never recomputed.
   */
  readonly kgPrice?: PurchaseKgPrice
}
export interface IncomingInvoice {
  readonly id: string; readonly companyId: string; readonly version: string
  readonly status: 'Recorded' | 'Matched' | 'FinancePending' | 'FinanceAccepted'
  readonly supplierPartyId: string; readonly companyAccountId: string; readonly currencyId: string; readonly functionalCurrencyId: string
  readonly moneyScale: number; readonly functionalScale: number; readonly officialRate: string | null
  readonly officialNumber: string; readonly ettn: string; readonly invoiceDate: string; readonly documentId: string; readonly documentVersion: string
  readonly amounts: InvoiceAmounts; readonly functionalAmounts: InvoiceAmounts | null; readonly lines: readonly IncomingInvoiceLine[]
  readonly variances: readonly { readonly invoiceLineId: string; readonly kind: string; readonly expected: string; readonly actual: string }[]
  readonly varianceDecision: { readonly approvedBy: string; readonly reason: string; readonly approvedAtUtc: string } | null
  readonly finalizedVersion: string | null
  /** AK-3b (SAT-D436): the document-level kg price indicator; hidden with a reason code while a service or expense line exists. */
  readonly kgPrice?: PurchaseKgPrice
}
export interface InvoicePage { readonly pageIndex: number; readonly hasNext: boolean; readonly items: readonly IncomingInvoice[] }
export interface InvoiceReceiptCandidate {
  readonly purchaseOrderId: string; readonly purchaseOrderNumber: string | null; readonly purchaseOrderLineId: string
  readonly orderLineNo: number; readonly acceptanceId: string; readonly acceptedAtUtc: string
  readonly productId: string; readonly unitId: string; readonly availableQuantity: string
  readonly serviceTypeId?: string
}
export interface InvoiceReceiptPage {
  readonly invoiceId: string; readonly invoiceLineId: string; readonly invoiceVersion: string
  readonly pageIndex: number; readonly hasNext: boolean; readonly items: readonly InvoiceReceiptCandidate[]
}
export interface InvoiceReceiptSelection { readonly invoiceLineId: string; readonly purchaseOrderId: string; readonly acceptanceId: string; readonly quantity: string }
export interface InvoiceAdditionalCostSelection { readonly invoiceLineId: string; readonly purchaseOrderId: string; readonly costId: string; readonly netAmount: string }
export interface InvoiceAdditionalCostCandidate { readonly costId: string; readonly purchaseOrderId: string; readonly purchaseOrderNumber: string | null;
  readonly description: string; readonly expenseTypeId: string | null; readonly expenseTypeName: string | null; readonly businessDate: string;
  readonly netAmount: string; readonly availableNetAmount: string; readonly vatPercent: string }
export interface InvoiceAdditionalCostPage { readonly invoiceId: string; readonly invoiceLineId: string; readonly invoiceVersion: string; readonly currencyId: string;
  readonly pageIndex: number; readonly hasNext: boolean; readonly items: readonly InvoiceAdditionalCostCandidate[] }
export type InvoiceCommand = { readonly action: 'matching'; readonly request: { readonly expectedVersion: string; readonly receipts: readonly InvoiceReceiptSelection[];
    readonly additionalCosts?: readonly InvoiceAdditionalCostSelection[] } }
  | { readonly action: 'variance-approval'; readonly request: { readonly expectedVersion: string; readonly reason: string } }
  | { readonly action: 'finalization' | 'finance-admission'; readonly request: { readonly expectedVersion: string } }
export interface InvoiceCommandResponse { readonly invoice: IncomingInvoice; readonly isReplay: boolean; readonly originalCorrelationId: string; readonly financeOutcome: string | null }
type Obj = Record<string, unknown>
const obj = (value: unknown): Obj | null => typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Obj : null
const exact = (value: Obj, fields: readonly string[]) => Object.keys(value).length === fields.length && fields.every((key) => Object.hasOwn(value, key))
const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.trim() === value
const decimal = (value: unknown): value is string => typeof value === 'string' && /^(0|[1-9]\d*)(\.\d+)?$/.test(value)
const instant = (value: unknown) => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T.*(Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value))
const scale = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 28
const positiveInt = (value: unknown) => typeof value === 'number' && Number.isSafeInteger(value) && value > 0
export const invoiceQuantity = (value: unknown): value is string => typeof value === 'string'
  && /^(0|[1-9]\d{0,21})(\.[0-9]{1,6})?$/.test(value) && !/^0(?:\.0+)?$/.test(value)
function amounts(value: unknown, precision: number): boolean {
  const item = obj(value)
  return item !== null && exact(item, ['net', 'vat', 'withholding', 'payable'])
    && Object.values(item).every((amount) => decimal(amount) && (precision === 0 ? !amount.includes('.') : amount.split('.')[1]?.length === precision))
}
/** Verify the wire shape and bindings; never recompute tax, money, rates or allocation totals. */
export function decodeIncomingInvoice(value: unknown, companyId: string, invoiceId?: string): IncomingInvoice | null {
  const item = obj(value)
  if (item === null || !exact(item, ['id', 'companyId', 'version', 'status', 'supplierPartyId', 'companyAccountId', 'currencyId', 'functionalCurrencyId',
    'moneyScale', 'functionalScale', 'officialRate', 'officialNumber', 'ettn', 'invoiceDate', 'documentId', 'documentVersion', 'amounts',
    'functionalAmounts', 'lines', 'variances', 'varianceDecision', 'finalizedVersion', ...(Object.hasOwn(item, 'kgPrice') ? ['kgPrice'] : [])])
    || (Object.hasOwn(item, 'kgPrice') && decodePurchaseKgPrice(item.kgPrice) === null)
    || item.companyId !== companyId || !guid(item.id) || (invoiceId !== undefined && item.id !== invoiceId) || !version(item.version)
    || !['Recorded', 'Matched', 'FinancePending', 'FinanceAccepted'].includes(String(item.status))
    || ![item.supplierPartyId, item.companyAccountId, item.currencyId, item.functionalCurrencyId, item.ettn, item.documentId].every(guid)
    || !scale(item.moneyScale) || !scale(item.functionalScale) || !version(item.documentVersion)
    || !text(item.officialNumber) || typeof item.invoiceDate !== 'string' || !isDateOnly(item.invoiceDate)
    || (item.officialRate !== null && (!decimal(item.officialRate) || /^0(?:\.0+)?$/.test(item.officialRate)))
    || !amounts(item.amounts, item.moneyScale) || (item.functionalAmounts !== null && !amounts(item.functionalAmounts, item.functionalScale))
    || !Array.isArray(item.lines) || item.lines.length === 0 || !Array.isArray(item.variances)
    || (item.finalizedVersion !== null && !version(item.finalizedVersion))
    || (['FinancePending', 'FinanceAccepted'].includes(String(item.status)) ? item.finalizedVersion === null : item.finalizedVersion !== null)) return null
  const ids = new Set<string>(), numbers = new Set<number>()
  for (const raw of item.lines) {
    const line = obj(raw)
    if (line === null || !exact(line, ['id', 'lineNo', 'productId', 'unitId', 'quantity', 'itemType', 'linePurpose', 'vatPercent', 'amounts', 'functionalAmounts',
      ...(Object.hasOwn(line, 'serviceTypeId') ? ['serviceTypeId'] : []), ...(Object.hasOwn(line, 'expenseTypeId') ? ['expenseTypeId'] : []),
      ...(Object.hasOwn(line, 'kgPrice') ? ['kgPrice'] : [])])
      // AK-3b: a kg price is exact or the whole invoice is refused; a service or expense line never carries one.
      || (Object.hasOwn(line, 'kgPrice') && (decodePurchaseKgPrice(line.kgPrice) === null || Object.hasOwn(line, 'serviceTypeId')
        || line.itemType === 'Expense' || line.linePurpose === 'expense' || line.linePurpose === 'service'))
      || !guid(line.id) || ids.has(line.id) || !positiveInt(line.lineNo) || numbers.has(line.lineNo as number)
      || (line.itemType === 'Expense' && line.linePurpose === 'expense'
        ? line.productId !== '00000000-0000-0000-0000-000000000000' || Object.hasOwn(line, 'serviceTypeId')
          || Object.hasOwn(line, 'expenseTypeId') && !guid(line.expenseTypeId)
        : Object.hasOwn(line, 'expenseTypeId') || line.itemType === 'Expense' || line.linePurpose === 'expense'
          || (Object.hasOwn(line, 'serviceTypeId') ? !guid(line.serviceTypeId) || line.productId !== '00000000-0000-0000-0000-000000000000'
            || line.itemType !== 'Service' || line.linePurpose !== 'service' : !guid(line.productId)))
      || !guid(line.unitId) || !invoiceQuantity(line.quantity) || !text(line.itemType) || !text(line.linePurpose)
      || !decimal(line.vatPercent) || !amounts(line.amounts, item.moneyScale)
      || (line.functionalAmounts !== null && !amounts(line.functionalAmounts, item.functionalScale))) return null
    ids.add(line.id); numbers.add(line.lineNo as number)
  }
  for (const raw of item.variances) {
    const variance = obj(raw)
    if (variance === null || !exact(variance, ['invoiceLineId', 'kind', 'expected', 'actual']) || !guid(variance.invoiceLineId)
      || !ids.has(variance.invoiceLineId) || (variance.kind !== 'Price' && variance.kind !== 'ExchangeRate' && variance.kind !== 'Quantity') || !decimal(variance.expected) || !decimal(variance.actual)) return null
  }
  if (item.varianceDecision !== null) {
    const decision = obj(item.varianceDecision)
    if (decision === null || !exact(decision, ['approvedBy', 'reason', 'approvedAtUtc']) || !text(decision.approvedBy)
      || !text(decision.reason) || !instant(decision.approvedAtUtc)) return null
  }
  return item as unknown as IncomingInvoice
}
export function decodeInvoicePage(value: unknown, companyId: string, pageIndex: number): InvoicePage | null {
  const page = obj(value)
  if (page === null || !exact(page, ['pageIndex', 'hasNext', 'items']) || page.pageIndex !== pageIndex || typeof page.hasNext !== 'boolean'
    || !Array.isArray(page.items) || page.items.length > 25) return null
  const items = page.items.map((item) => decodeIncomingInvoice(item, companyId))
  if (items.some((item) => item === null) || new Set(items.map((item) => item?.id)).size !== items.length) return null
  return { pageIndex, hasNext: page.hasNext, items: items as IncomingInvoice[] }
}
export function decodeInvoiceReceipts(value: unknown, invoice: IncomingInvoice, line: IncomingInvoiceLine, pageIndex: number): InvoiceReceiptPage | null {
  const page = obj(value)
  if (page === null || !exact(page, ['invoiceId', 'invoiceLineId', 'invoiceVersion', 'pageIndex', 'hasNext', 'items'])
    || page.invoiceId !== invoice.id || page.invoiceLineId !== line.id || page.invoiceVersion !== invoice.version
    || page.pageIndex !== pageIndex || typeof page.hasNext !== 'boolean' || !Array.isArray(page.items) || page.items.length > 25) return null
  const ids = new Set<string>()
  for (const raw of page.items) {
    const item = obj(raw)
    if (item === null || !exact(item, ['purchaseOrderId', 'purchaseOrderNumber', 'purchaseOrderLineId', 'orderLineNo', 'acceptanceId',
      'acceptedAtUtc', 'productId', 'unitId', 'availableQuantity', ...(Object.hasOwn(item, 'serviceTypeId') ? ['serviceTypeId'] : [])])
      || !guid(item.purchaseOrderId) || !guid(item.purchaseOrderLineId)
      || !guid(item.acceptanceId) || ids.has(item.acceptanceId) || !positiveInt(item.orderLineNo)
      || (item.purchaseOrderNumber !== null && !text(item.purchaseOrderNumber)) || !instant(item.acceptedAtUtc)
      || item.productId !== line.productId || item.unitId !== line.unitId || item.serviceTypeId !== line.serviceTypeId
      || !invoiceQuantity(item.availableQuantity)) return null
    ids.add(item.acceptanceId)
  }
  return page as unknown as InvoiceReceiptPage
}
export function decodeInvoiceCommand(value: unknown, companyId: string, invoiceId: string, replay: boolean): InvoiceCommandResponse | null {
  const item = obj(value)
  if (item === null || !exact(item, ['invoice', 'isReplay', 'originalCorrelationId', 'financeOutcome']) || item.isReplay !== replay
    || !text(item.originalCorrelationId) || (item.financeOutcome !== null && !text(item.financeOutcome))) return null
  const invoice = decodeIncomingInvoice(item.invoice, companyId, invoiceId)
  return invoice === null ? null : { invoice, isReplay: replay, originalCorrelationId: item.originalCorrelationId, financeOutcome: item.financeOutcome as string | null }
}
export function decodeInvoiceAdditionalCosts(value: unknown, invoice: IncomingInvoice, line: IncomingInvoiceLine, pageIndex: number): InvoiceAdditionalCostPage | null {
  const page = obj(value)
  if (line.itemType !== 'Expense' || line.linePurpose !== 'expense' || page === null
    || !exact(page, ['invoiceId', 'invoiceLineId', 'invoiceVersion', 'currencyId', 'pageIndex', 'hasNext', 'items'])
    || page.invoiceId !== invoice.id || page.invoiceLineId !== line.id || page.invoiceVersion !== invoice.version || page.currencyId !== invoice.currencyId
    || page.pageIndex !== pageIndex || typeof page.hasNext !== 'boolean' || !Array.isArray(page.items) || page.items.length > 25) return null
  const ids = new Set<string>()
  for (const raw of page.items) {
    const item = obj(raw)
    if (item === null || !exact(item, ['costId', 'purchaseOrderId', 'purchaseOrderNumber', 'description', 'expenseTypeId', 'expenseTypeName',
      'businessDate', 'netAmount', 'availableNetAmount', 'vatPercent']) || !guid(item.costId) || ids.has(item.costId) || !guid(item.purchaseOrderId)
      || item.purchaseOrderNumber !== null && !text(item.purchaseOrderNumber) || !text(item.description)
      || item.expenseTypeId !== (line.expenseTypeId ?? null)
      || (item.expenseTypeId === null ? item.expenseTypeName !== null : !guid(item.expenseTypeId) || !text(item.expenseTypeName))
      || typeof item.businessDate !== 'string' || !isDateOnly(item.businessDate) || !decimal(item.netAmount)
      || !invoiceQuantity(item.availableNetAmount) || item.vatPercent !== line.vatPercent) return null
    ids.add(item.costId)
  }
  return page as unknown as InvoiceAdditionalCostPage
}

export type PurchaseOrderStatus = 'Draft' | 'Approved'

export interface PurchaseOrderSummary {
  readonly id: string
  readonly companyId: string
  readonly status: PurchaseOrderStatus
  readonly version: string
  readonly supplierPartyId: string
  readonly companyAccountId: string
  readonly sourceRequestId: string | null
  readonly effectiveOn: string
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly lineCount: number
}

export interface PurchaseOrderLine {
  readonly lineId: string
  readonly lineNo: number
  readonly kind: string
  readonly parentLineId: string | null
  readonly sourceRequestLineId: string | null
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly ordered: string
  readonly technical: string
  readonly committed: string
  readonly accepted: string
  readonly remaining: string
}

export interface PurchaseOrderDetail {
  readonly order: PurchaseOrderSummary
  readonly lines: readonly PurchaseOrderLine[]
}

export interface PurchaseOrderList {
  readonly items: readonly PurchaseOrderSummary[]
  readonly nextCursor: string | null
}

export interface PurchaseOrderCommandResponse {
  readonly detail: PurchaseOrderDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const DATE = /^\d{4}-\d{2}-\d{2}$/
const DECIMAL = /^(0|[1-9]\d*)(\.\d+)?$/

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as JsonObject)
    : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}

function guid(value: unknown): value is string {
  return text(value) && GUID.test(value)
}

function nullableGuid(value: unknown): value is string | null {
  return value === null || guid(value)
}

function status(value: unknown): value is PurchaseOrderStatus {
  return value === 'Draft' || value === 'Approved'
}

function decodeSummary(value: unknown): PurchaseOrderSummary | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, [
      'id', 'companyId', 'status', 'version', 'supplierPartyId', 'companyAccountId',
      'sourceRequestId', 'effectiveOn', 'createdAtUtc', 'updatedAtUtc', 'lineCount',
    ]) ||
    !guid(source.id) || !guid(source.companyId) || !status(source.status) || !text(source.version) ||
    !guid(source.supplierPartyId) || !guid(source.companyAccountId) || !nullableGuid(source.sourceRequestId) ||
    !text(source.effectiveOn) || !DATE.test(source.effectiveOn) || !text(source.createdAtUtc) ||
    !text(source.updatedAtUtc) || !Number.isSafeInteger(source.lineCount) || (source.lineCount as number) < 0
  ) return null
  return source as unknown as PurchaseOrderSummary
}

function decodeLine(value: unknown): PurchaseOrderLine | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, [
      'lineId', 'lineNo', 'kind', 'parentLineId', 'sourceRequestLineId', 'productId',
      'productVersion', 'unitId', 'ordered', 'technical', 'committed', 'accepted', 'remaining',
    ]) ||
    !guid(source.lineId) || !Number.isSafeInteger(source.lineNo) || (source.lineNo as number) < 1 ||
    !text(source.kind) || !nullableGuid(source.parentLineId) || !nullableGuid(source.sourceRequestLineId) ||
    !guid(source.productId) || !text(source.productVersion) || !guid(source.unitId)
  ) return null
  for (const name of ['ordered', 'technical', 'committed', 'accepted', 'remaining'] as const) {
    if (!text(source[name]) || !DECIMAL.test(source[name] as string)) return null
  }
  return source as unknown as PurchaseOrderLine
}

export function decodePurchaseOrderList(value: unknown): PurchaseOrderList | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'nextCursor']) || !Array.isArray(source.items)) return null
  if (source.nextCursor !== null && !text(source.nextCursor)) return null
  const items = source.items.map(decodeSummary)
  return items.some((item) => item === null)
    ? null
    : { items: items as PurchaseOrderSummary[], nextCursor: source.nextCursor as string | null }
}

export function decodePurchaseOrderDetail(value: unknown): PurchaseOrderDetail | null {
  const source = record(value)
  if (source === null || !exact(source, ['order', 'lines']) || !Array.isArray(source.lines)) return null
  const order = decodeSummary(source.order)
  const lines = source.lines.map(decodeLine)
  if (order === null || lines.some((line) => line === null) || order.lineCount !== lines.length) return null
  return { order, lines: lines as PurchaseOrderLine[] }
}

export function decodePurchaseOrderCommandResponse(value: unknown): PurchaseOrderCommandResponse | null {
  const source = record(value)
  if (source === null || !exact(source, ['detail', 'isReplay', 'originalCorrelationId'])) return null
  const detail = decodePurchaseOrderDetail(source.detail)
  if (detail === null || typeof source.isReplay !== 'boolean' || !text(source.originalCorrelationId)) return null
  return { detail, isReplay: source.isReplay, originalCorrelationId: source.originalCorrelationId }
}

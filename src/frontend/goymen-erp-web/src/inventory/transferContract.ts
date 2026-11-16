export interface TransferSummary {
  readonly transferId: string; readonly companyId: string; readonly version: string; readonly status: string
  readonly sourceLocationId: string; readonly destinationLocationId: string
  readonly plannedAtUtc: string; readonly dispatchedAtUtc: string | null
}
export interface TransferDisplayItem {
  readonly transfer: TransferSummary; readonly sourceLocationCode: string; readonly sourceLocationName: string
  readonly destinationLocationCode: string; readonly destinationLocationName: string
}
export interface TransferList { readonly companyId: string; readonly items: readonly TransferDisplayItem[]; readonly windowFull: boolean }
export interface TransferLine {
  readonly lineId: string; readonly labelId: string; readonly expectedLabelVersion: string; readonly plannedKg: string
  readonly actualShippedKg: string | null; readonly differenceKg: string | null; readonly status: string
}
export interface TransferDetail extends TransferSummary {
  readonly sourceCustodianKind: string; readonly sourceCustodianId: string
  readonly destinationCustodianKind: string; readonly destinationCustodianId: string; readonly lines: readonly TransferLine[]
}

const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(v)
  && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v)
const text = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0
const version = (v: unknown): v is string => typeof v === 'string' && /^[1-9]\d{0,18}$/.test(v) && BigInt(v) <= 9223372036854775807n
const instant = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && /(?:Z|[+-]\d{2}:\d{2})$/.test(v) && !Number.isNaN(Date.parse(v))
const quantity = (v: unknown): v is string => typeof v === 'string' && /^\d+(?:\.\d{1,3})?$/.test(v)
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const exact = (v: Record<string, unknown>, fields: readonly string[]) => Object.keys(v).length === fields.length && fields.every(f => Object.hasOwn(v, f))
const statuses = ['Planned', 'InTransit', 'PartiallyAccepted', 'Accepted', 'DispatchCancelled', 'Closed']
const summaryFields = ['transferId', 'companyId', 'version', 'status', 'sourceLocationId', 'destinationLocationId', 'plannedAtUtc', 'dispatchedAtUtc'] as const
const validSummary = (v: Record<string, unknown>): boolean => guid(v.transferId) && guid(v.companyId) && version(v.version)
  && typeof v.status === 'string' && statuses.includes(v.status) && guid(v.sourceLocationId) && guid(v.destinationLocationId)
  && instant(v.plannedAtUtc) && (v.dispatchedAtUtc === null || instant(v.dispatchedAtUtc))

export function decodeTransferList(value: unknown): TransferList | null {
  if (!record(value) || !exact(value, ['companyId', 'items', 'windowFull']) || !guid(value.companyId)
    || !Array.isArray(value.items) || typeof value.windowFull !== 'boolean') return null
  const ids = new Set<string>()
  for (const row of value.items) {
    if (!record(row) || !exact(row, ['transfer', 'sourceLocationCode', 'sourceLocationName', 'destinationLocationCode', 'destinationLocationName'])
      || !record(row.transfer) || !exact(row.transfer, summaryFields) || !validSummary(row.transfer)
      || row.transfer.companyId !== value.companyId || !text(row.sourceLocationCode) || !text(row.sourceLocationName)
      || !text(row.destinationLocationCode) || !text(row.destinationLocationName) || ids.has(row.transfer.transferId as string)) return null
    ids.add(row.transfer.transferId as string)
  }
  return value as unknown as TransferList
}

export function decodeTransferDetail(value: unknown): TransferDetail | null {
  if (!record(value) || !exact(value, [...summaryFields, 'sourceCustodianKind', 'sourceCustodianId', 'destinationCustodianKind', 'destinationCustodianId', 'lines'])
    || !validSummary(value) || !['Company', 'Counterparty'].includes(String(value.sourceCustodianKind)) || !guid(value.sourceCustodianId)
    || !['Company', 'Counterparty'].includes(String(value.destinationCustodianKind)) || !guid(value.destinationCustodianId) || !Array.isArray(value.lines)) return null
  const ids = new Set<string>()
  for (const line of value.lines) {
    if (!record(line) || !exact(line, ['lineId', 'labelId', 'expectedLabelVersion', 'plannedKg', 'actualShippedKg', 'differenceKg', 'status'])
      || !guid(line.lineId) || !guid(line.labelId) || !version(line.expectedLabelVersion) || !quantity(line.plannedKg)
      || !(line.actualShippedKg === null || quantity(line.actualShippedKg))
      || !(line.differenceKg === null || typeof line.differenceKg === 'string' && /^-?\d+(?:\.\d{1,3})?$/.test(line.differenceKg))
      || !['Planned', 'InTransit', 'Accepted', 'WrittenOff', 'DispatchCancelled'].includes(String(line.status)) || ids.has(line.lineId)) return null
    ids.add(line.lineId)
  }
  return value as unknown as TransferDetail
}

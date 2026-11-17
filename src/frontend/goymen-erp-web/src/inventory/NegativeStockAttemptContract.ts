/** SAT-D328/D336..D339. Every quantity is the owner's recorded figure; this client computes no shortfall. */
export const NEGATIVE_STOCK_ATTEMPT_LIMIT = 50
export interface NegativeStockAttemptQuery {
  readonly productId: string | null
  readonly locationId: string | null
  readonly openOnly: boolean
}
export interface NegativeStockAttemptResolution {
  readonly stockCountId: string
  readonly decidedAtUtc: string
  readonly decidedBy: string
}
export interface NegativeStockAttempt {
  readonly attemptId: string
  readonly companyId: string
  readonly movementId: string
  readonly commandName: string
  readonly actorId: string
  readonly occurredAtUtc: string
  readonly productId: string
  readonly locationId: string
  readonly labelId: string
  readonly labelCode: string
  readonly movementKind: string
  readonly sourceKind: string
  readonly sourceId: string
  readonly sourceLineId: string | null
  readonly requestedKg: string
  readonly availableKg: string
  readonly missingKg: string
  readonly resolution: NegativeStockAttemptResolution | null
}
export interface NegativeStockAttemptPage {
  readonly companyId: string
  readonly items: readonly NegativeStockAttempt[]
  readonly windowFull: boolean
}
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL = '00000000-0000-0000-0000-000000000000'
/** The owner persists these snapshots as decimal(18,3); grammar checks preserve the string unchanged. */
const KG = /^-?(0|[1-9]\d{0,14})(\.\d{0,2}[1-9])?$/
const UTC = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$/
const CONTROL = /[\u0000-\u001f\u007f-\u009f\u200b-\u200f\u202a-\u202e\u2066-\u2069]/
const rowFields = ['attemptId', 'companyId', 'movementId', 'commandName', 'actorId', 'occurredAtUtc', 'productId', 'locationId',
  'labelId', 'labelCode', 'movementKind', 'sourceKind', 'sourceId', 'sourceLineId', 'requestedKg', 'availableKg', 'missingKg', 'resolution'] as const
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const exact = (value: Record<string, unknown>, fields: readonly string[]) => Object.keys(value).length === fields.length && fields.every((key) => Object.hasOwn(value, key))
export const isNegativeStockAttemptId = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL
export const sameNegativeStockAttemptId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
const text = (value: unknown, maximum: number): value is string => typeof value === 'string' && value !== '' && value === value.trim()
  && value.length <= maximum && !CONTROL.test(value)
const instant = (value: unknown): value is string => typeof value === 'string' && UTC.test(value) && Number.isFinite(Date.parse(value))
export const isNegativeStockAttemptKg = (value: unknown, positive = false): value is string => typeof value === 'string'
  && value.length <= 20 && KG.test(value) && value !== '-0' && (!positive || (value !== '0' && !value.startsWith('-')))
export function validNegativeStockAttemptQuery(query: NegativeStockAttemptQuery): boolean {
  return typeof query.openOnly === 'boolean' && (query.productId === null || isNegativeStockAttemptId(query.productId))
    && (query.locationId === null || isNegativeStockAttemptId(query.locationId))
}
function decodeRow(value: unknown, companyId: string, query: NegativeStockAttemptQuery): NegativeStockAttempt | null {
  if (!record(value) || !exact(value, rowFields) || !isNegativeStockAttemptId(value.companyId)
    || !sameNegativeStockAttemptId(value.companyId, companyId)
    || !isNegativeStockAttemptId(value.attemptId) || !isNegativeStockAttemptId(value.movementId)
    || !isNegativeStockAttemptId(value.productId) || !isNegativeStockAttemptId(value.locationId)
    || !isNegativeStockAttemptId(value.labelId) || !isNegativeStockAttemptId(value.sourceId)
    || !(value.sourceLineId === null || isNegativeStockAttemptId(value.sourceLineId))
    || !text(value.commandName, 128) || !text(value.actorId, 128) || !instant(value.occurredAtUtc)
    || !text(value.labelCode, 64) || !text(value.movementKind, 64) || !text(value.sourceKind, 64)
    || !isNegativeStockAttemptKg(value.requestedKg, true) || !isNegativeStockAttemptKg(value.availableKg)
    || !isNegativeStockAttemptKg(value.missingKg, true)
    || (query.productId !== null && !sameNegativeStockAttemptId(value.productId, query.productId))
    || (query.locationId !== null && !sameNegativeStockAttemptId(value.locationId, query.locationId))) return null
  let resolution: NegativeStockAttemptResolution | null = null
  if (value.resolution !== null) {
    if (query.openOnly || !record(value.resolution) || !exact(value.resolution, ['stockCountId', 'decidedAtUtc', 'decidedBy'])
      || !isNegativeStockAttemptId(value.resolution.stockCountId) || !instant(value.resolution.decidedAtUtc)
      || !text(value.resolution.decidedBy, 128)) return null
    resolution = Object.freeze({ stockCountId: value.resolution.stockCountId, decidedAtUtc: value.resolution.decidedAtUtc, decidedBy: value.resolution.decidedBy })
  }
  return Object.freeze({ attemptId: value.attemptId, companyId: value.companyId, movementId: value.movementId,
    commandName: value.commandName, actorId: value.actorId, occurredAtUtc: value.occurredAtUtc, productId: value.productId,
    locationId: value.locationId, labelId: value.labelId, labelCode: value.labelCode, movementKind: value.movementKind,
    sourceKind: value.sourceKind, sourceId: value.sourceId, sourceLineId: value.sourceLineId, requestedKg: value.requestedKg,
    availableKg: value.availableKg, missingKg: value.missingKg, resolution })
}
export function decodeNegativeStockAttemptPage(value: unknown, companyId: string, query: NegativeStockAttemptQuery): NegativeStockAttemptPage | null {
  if (!isNegativeStockAttemptId(companyId) || !validNegativeStockAttemptQuery(query) || !record(value)
    || !exact(value, ['companyId', 'items', 'windowFull']) || !isNegativeStockAttemptId(value.companyId)
    || !sameNegativeStockAttemptId(value.companyId, companyId) || typeof value.windowFull !== 'boolean'
    || !Array.isArray(value.items) || value.items.length > NEGATIVE_STOCK_ATTEMPT_LIMIT) return null
  const items: NegativeStockAttempt[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    const row = decodeRow(raw, companyId, query)
    if (row === null || seen.has(row.attemptId.toLowerCase())) return null
    seen.add(row.attemptId.toLowerCase()); items.push(row)
  }
  return Object.freeze({ companyId: value.companyId, items: Object.freeze(items), windowFull: value.windowFull })
}

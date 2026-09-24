export interface InventoryLabelDocument {
  readonly kind: string
  readonly attachedAtUtc: string
}

export interface InventoryLabelPrint {
  readonly sequence: number
  readonly printedAtUtc: string
  readonly reason: string | null
}

export interface InventoryLabelLot {
  readonly id: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
}

export interface InventoryLabelIdentity {
  readonly labelId: string
  readonly companyId: string
  readonly code: string
  readonly productId: string
  readonly lotId: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly documentStatus: string
  readonly requiredDocuments: readonly string[]
  readonly documents: readonly InventoryLabelDocument[]
  readonly prints: readonly InventoryLabelPrint[]
  readonly lot: InventoryLabelLot
}

export interface InventoryLabelPosition {
  readonly ownerKind: string
  readonly ownerId: string
  readonly custodianKind: string
  readonly custodianId: string
  readonly locationId: string
  readonly productId: string
  readonly block: string
  readonly quantityKg: string
}

export interface InventoryLabelBalances {
  readonly labelId: string
  readonly physicalKg: string
  readonly freeKg: string
  readonly blockedKg: string
  readonly positions: readonly InventoryLabelPosition[]
}

export interface InventoryLabelDetail {
  readonly identity: InventoryLabelIdentity
  readonly balances: InventoryLabelBalances
}

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const DECIMAL = /^-?(0|[1-9]\d*)(\.\d*[1-9])?$/

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as JsonObject
    : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function guid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID
}

function nullableText(value: unknown): value is string | null {
  return value === null || text(value)
}

function instant(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0 && Number.isFinite(Date.parse(value))
}

function quantity(value: unknown): value is string {
  return typeof value === 'string' && DECIMAL.test(value)
}

function document(value: unknown): InventoryLabelDocument | null {
  const source = record(value)
  if (source === null || !exact(source, ['kind', 'attachedAtUtc']) || !text(source.kind) || !instant(source.attachedAtUtc)) return null
  return source as unknown as InventoryLabelDocument
}

function print(value: unknown): InventoryLabelPrint | null {
  const source = record(value)
  if (source === null || !exact(source, ['sequence', 'printedAtUtc', 'reason']) ||
    !Number.isSafeInteger(source.sequence) || (source.sequence as number) < 1 ||
    !instant(source.printedAtUtc) || !nullableText(source.reason)) return null
  return source as unknown as InventoryLabelPrint
}

function lot(value: unknown): InventoryLabelLot | null {
  const source = record(value)
  if (source === null || !exact(source, ['id', 'heatNumber', 'coilNumber']) || !guid(source.id) ||
    !nullableText(source.heatNumber) || !nullableText(source.coilNumber)) return null
  return source as unknown as InventoryLabelLot
}

function position(value: unknown): InventoryLabelPosition | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'ownerKind', 'ownerId', 'custodianKind', 'custodianId', 'locationId', 'productId', 'block', 'quantityKg',
  ]) || !text(source.ownerKind) || !guid(source.ownerId) || !text(source.custodianKind) ||
    !guid(source.custodianId) || !guid(source.locationId) || !guid(source.productId) ||
    !text(source.block) || !quantity(source.quantityKg) || source.quantityKg === '0') return null
  return source as unknown as InventoryLabelPosition
}

export function decodeInventoryLabelIdentity(value: unknown): InventoryLabelIdentity | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'labelId', 'companyId', 'code', 'productId', 'lotId', 'heatNumber', 'coilNumber', 'documentStatus',
    'requiredDocuments', 'documents', 'prints', 'lot',
  ]) || !guid(source.labelId) || !guid(source.companyId) || !text(source.code) || !guid(source.productId) ||
    !guid(source.lotId) || !nullableText(source.heatNumber) || !nullableText(source.coilNumber) ||
    !text(source.documentStatus) || !Array.isArray(source.requiredDocuments) ||
    source.requiredDocuments.some((item) => !text(item)) || !Array.isArray(source.documents) ||
    !Array.isArray(source.prints)) return null

  const decodedLot = lot(source.lot)
  const documents = source.documents.map(document)
  const prints = source.prints.map(print)
  if (decodedLot === null || decodedLot.id !== source.lotId || documents.some((item) => item === null) ||
    prints.some((item) => item === null) || new Set(source.requiredDocuments).size !== source.requiredDocuments.length ||
    new Set((prints as InventoryLabelPrint[]).map((item) => item.sequence)).size !== prints.length) return null

  return {
    ...(source as unknown as InventoryLabelIdentity),
    lot: decodedLot,
    documents: documents as InventoryLabelDocument[],
    prints: prints as InventoryLabelPrint[],
  }
}

export function decodeInventoryLabelBalances(value: unknown): InventoryLabelBalances | null {
  const source = record(value)
  if (source === null || !exact(source, ['labelId', 'physicalKg', 'freeKg', 'blockedKg', 'positions']) ||
    !guid(source.labelId) || !quantity(source.physicalKg) || !quantity(source.freeKg) ||
    !quantity(source.blockedKg) || !Array.isArray(source.positions)) return null
  const positions = source.positions.map(position)
  if (positions.some((item) => item === null)) return null
  return { ...(source as unknown as InventoryLabelBalances), positions: positions as InventoryLabelPosition[] }
}

export function combineInventoryLabelDetail(
  identity: InventoryLabelIdentity,
  balances: InventoryLabelBalances,
  expectedCompanyId: string,
  expectedLabelId: string,
): InventoryLabelDetail | null {
  if (identity.companyId !== expectedCompanyId || identity.labelId !== expectedLabelId ||
    balances.labelId !== expectedLabelId || balances.positions.some((item) => item.productId !== identity.productId)) return null
  return { identity, balances }
}

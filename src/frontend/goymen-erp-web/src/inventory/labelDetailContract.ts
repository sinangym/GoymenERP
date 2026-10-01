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

export interface InventoryLabelGenealogyEdge {
  readonly transformationId: string
  readonly kind: 'Split' | 'Merge'
  readonly occurredAtUtc: string
  readonly sourceLabelId: string
  readonly sourceCode: string
  readonly sourceLabelVersion: string
  readonly sourceQuantityKg: string
  readonly outputLabelId: string
  readonly outputCode: string
  readonly outputLabelVersion: string
  readonly outputQuantityKg: string
}

export interface InventoryLabelGenealogy {
  readonly labelId: string
  readonly labelCode: string
  readonly edges: readonly InventoryLabelGenealogyEdge[]
  readonly windowFull: boolean
}

export interface InventoryLabelMovementLine {
  readonly sequence: number
  readonly direction: 'In' | 'Out'
  readonly ownerKind: 'Company' | 'Counterparty'
  readonly ownerId: string
  readonly custodianKind: 'Company' | 'Counterparty'
  readonly custodianId: string
  readonly locationId: string
  readonly productId: string
  readonly block: 'Free' | 'Blocked'
  readonly quantityKg: string
}

export interface InventoryLabelMovement {
  readonly movementId: string
  readonly kind: 'Receipt' | 'Issue' | 'Block' | 'Release' | 'Return' | 'CountAdjustment' | 'Scrap' |
    'TransferDispatch' | 'TransferAccept'
  readonly occurredAtUtc: string
  readonly sourceKind: string
  readonly sourceId: string
  readonly sourceLineId: string | null
  readonly sourceGrain: 'Whole' | 'Line'
  readonly lines: readonly InventoryLabelMovementLine[]
}

export interface InventoryLabelMovementTimeline {
  readonly labelId: string
  readonly entries: readonly InventoryLabelMovement[]
  readonly windowFull: boolean
}

export interface InventoryLabelDetail {
  readonly labelVersion: string
  readonly identity: InventoryLabelIdentity
  readonly balances: InventoryLabelBalances
  readonly genealogy: InventoryLabelGenealogy
}

export interface InventoryLabelDetailWithMovementTimeline extends InventoryLabelDetail {
  readonly movementTimeline: InventoryLabelMovementTimeline
}

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const DECIMAL = /^(0|[1-9]\d*)(\.\d{0,2}[1-9])?$/
const VERSION = /^[1-9]\d*$/
const MAX_INT64 = 9223372036854775807n

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

function positiveQuantity(value: unknown): value is string {
  return quantity(value) && value !== '0'
}

function version(value: unknown): value is string {
  return typeof value === 'string' && VERSION.test(value) && value.length <= 19 && BigInt(value) <= MAX_INT64
}

function decimalParts(value: string): readonly [bigint, number] {
  const [whole, fraction = ''] = value.split('.')
  return [BigInt(`${whole}${fraction}`), fraction.length]
}

function decimalSumsEqual(left: readonly string[], right: readonly string[]): boolean {
  const parts = [...left, ...right].map(decimalParts)
  const scale = Math.max(...parts.map(([, digits]) => digits))
  const scaled = ([units, digits]: readonly [bigint, number]) => units * (10n ** BigInt(scale - digits))
  const sum = (values: readonly string[]) => values.map(decimalParts)
    .reduce((total, value) => total + scaled(value), 0n)
  return sum(left) === sum(right)
}

const decimalSumEquals = (total: string, values: readonly string[]): boolean => decimalSumsEqual([total], values)

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
    (source.block !== 'Free' && source.block !== 'Blocked') || !positiveQuantity(source.quantityKg)) return null
  return source as unknown as InventoryLabelPosition
}

function genealogyEdge(value: unknown): InventoryLabelGenealogyEdge | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'transformationId', 'kind', 'occurredAtUtc', 'sourceLabelId', 'sourceCode', 'sourceLabelVersion',
    'sourceQuantityKg', 'outputLabelId', 'outputCode', 'outputLabelVersion', 'outputQuantityKg',
  ]) || !guid(source.transformationId) || (source.kind !== 'Split' && source.kind !== 'Merge') ||
    !instant(source.occurredAtUtc) || !guid(source.sourceLabelId) || !text(source.sourceCode) ||
    !version(source.sourceLabelVersion) || !positiveQuantity(source.sourceQuantityKg) ||
    !guid(source.outputLabelId) || !text(source.outputCode) || !version(source.outputLabelVersion) ||
    !positiveQuantity(source.outputQuantityKg)) return null
  return source as unknown as InventoryLabelGenealogyEdge
}

function movementLine(value: unknown): InventoryLabelMovementLine | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'sequence', 'direction', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId',
    'locationId', 'productId', 'block', 'quantityKg',
  ]) || !Number.isSafeInteger(source.sequence) || (source.sequence as number) < 1 ||
    (source.direction !== 'In' && source.direction !== 'Out') ||
    (source.ownerKind !== 'Company' && source.ownerKind !== 'Counterparty') || !guid(source.ownerId) ||
    (source.custodianKind !== 'Company' && source.custodianKind !== 'Counterparty') || !guid(source.custodianId) ||
    !guid(source.locationId) || !guid(source.productId) ||
    (source.block !== 'Free' && source.block !== 'Blocked') || !positiveQuantity(source.quantityKg)) return null
  return source as unknown as InventoryLabelMovementLine
}

function movement(value: unknown): InventoryLabelMovement | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'movementId', 'kind', 'occurredAtUtc', 'sourceKind', 'sourceId', 'sourceLineId', 'sourceGrain', 'lines',
  ]) || !guid(source.movementId) ||
    !['Receipt', 'Issue', 'Block', 'Release', 'Return', 'CountAdjustment', 'Scrap',
      'TransferDispatch', 'TransferAccept'].includes(source.kind as string) ||
    !instant(source.occurredAtUtc) || !text(source.sourceKind) || source.sourceKind !== source.sourceKind.trim() ||
    source.sourceKind.length > 64 || !guid(source.sourceId) ||
    (source.sourceLineId !== null && !guid(source.sourceLineId)) ||
    (source.sourceGrain !== 'Whole' && source.sourceGrain !== 'Line') ||
    (source.sourceGrain === 'Whole') !== (source.sourceLineId === null) || !Array.isArray(source.lines)) return null
  const lines = source.lines.map(movementLine)
  if (lines.length === 0 || lines.some((line) => line === null)) return null
  const decoded = lines as InventoryLabelMovementLine[]
  if (new Set(decoded.map((line) => line.sequence)).size !== decoded.length ||
    decoded.some((line, index) => index > 0 && decoded[index - 1].sequence >= line.sequence) ||
    ((source.kind === 'Receipt' || source.kind === 'Return' || source.kind === 'TransferAccept') &&
      decoded.some((line) => line.direction !== 'In')) ||
    source.kind === 'Issue' && decoded.some((line) => line.direction !== 'Out' || line.block === 'Blocked') ||
    source.kind === 'TransferDispatch' && decoded.some((line) => line.direction !== 'Out') ||
    source.kind === 'Scrap' && (decoded.length !== 1 || decoded[0].direction !== 'Out') ||
    (source.kind === 'Block' || source.kind === 'Release') && !validBlockChange(source.kind, decoded)) return null
  return { ...(source as unknown as InventoryLabelMovement), lines: decoded }
}

function validBlockChange(kind: 'Block' | 'Release', lines: readonly InventoryLabelMovementLine[]): boolean {
  if (lines.length !== 2) return false
  const from = lines.filter((line) => line.direction === 'Out')
  const to = lines.filter((line) => line.direction === 'In')
  if (from.length !== 1 || to.length !== 1) return false
  return from[0].block === (kind === 'Block' ? 'Free' : 'Blocked') &&
    to[0].block === (kind === 'Block' ? 'Blocked' : 'Free') &&
    from[0].ownerKind === to[0].ownerKind && from[0].ownerId === to[0].ownerId &&
    from[0].custodianKind === to[0].custodianKind && from[0].custodianId === to[0].custodianId &&
    from[0].locationId === to[0].locationId && from[0].productId === to[0].productId &&
    from[0].quantityKg === to[0].quantityKg
}

interface GenealogyLabelFacts {
  readonly code: string
  readonly version: string
  readonly quantityKg: string
}

function sameGenealogyLabelFacts(
  labels: Map<string, GenealogyLabelFacts>,
  labelId: string,
  code: string,
  versionValue: string,
  quantityKg: string,
): boolean {
  const existing = labels.get(labelId)
  if (existing !== undefined) {
    return existing.code === code && existing.version === versionValue && existing.quantityKg === quantityKg
  }
  labels.set(labelId, { code, version: versionValue, quantityKg })
  return true
}

function validGenealogyTransformations(edges: readonly InventoryLabelGenealogyEdge[]): boolean {
  const groups = new Map<string, InventoryLabelGenealogyEdge[]>()
  for (const edge of edges) {
    const group = groups.get(edge.transformationId)
    if (group === undefined) groups.set(edge.transformationId, [edge])
    else group.push(edge)
  }

  for (const group of groups.values()) {
    const first = group[0]
    const pairs = new Set<string>()
    const sources = new Map<string, GenealogyLabelFacts>()
    const outputs = new Map<string, GenealogyLabelFacts>()
    for (const edge of group) {
      const pair = `${edge.sourceLabelId}:${edge.outputLabelId}`
      if (edge.kind !== first.kind || edge.occurredAtUtc !== first.occurredAtUtc || pairs.has(pair) ||
        !sameGenealogyLabelFacts(sources, edge.sourceLabelId, edge.sourceCode,
          edge.sourceLabelVersion, edge.sourceQuantityKg) ||
        !sameGenealogyLabelFacts(outputs, edge.outputLabelId, edge.outputCode,
          edge.outputLabelVersion, edge.outputQuantityKg)) return false
      pairs.add(pair)
    }
    if ([...sources.keys()].some((labelId) => outputs.has(labelId)) ||
      (first.kind === 'Split' && (sources.size !== 1 || outputs.size < 2)) ||
      (first.kind === 'Merge' && (sources.size < 2 || outputs.size !== 1)) ||
      !decimalSumsEqual(
        [...sources.values()].map((facts) => facts.quantityKg),
        [...outputs.values()].map((facts) => facts.quantityKg),
      )) return false
  }
  return true
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
  const decoded = positions as InventoryLabelPosition[]
  if (!decimalSumEquals(source.physicalKg, decoded.map((item) => item.quantityKg)) ||
    !decimalSumEquals(source.freeKg, decoded.filter((item) => item.block === 'Free').map((item) => item.quantityKg)) ||
    !decimalSumEquals(source.blockedKg, decoded.filter((item) => item.block === 'Blocked').map((item) => item.quantityKg))) return null
  return { ...(source as unknown as InventoryLabelBalances), positions: decoded }
}

export function decodeInventoryLabelGenealogy(
  value: unknown,
  expectedLabelId: string,
  expectedLabelCode: string,
): InventoryLabelGenealogy | null {
  const source = record(value)
  if (source === null || !exact(source, ['labelId', 'labelCode', 'edges', 'windowFull']) ||
    source.labelId !== expectedLabelId || source.labelCode !== expectedLabelCode ||
    !Array.isArray(source.edges) || typeof source.windowFull !== 'boolean') return null
  const edges = source.edges.map(genealogyEdge)
  if (edges.some((item) => item === null)) return null
  const decoded = edges as InventoryLabelGenealogyEdge[]
  if (!validGenealogyTransformations(decoded)) return null
  const connected = new Set([expectedLabelId])
  const remaining = [...decoded]
  while (remaining.length > 0) {
    const index = remaining.findIndex((edge) => connected.has(edge.sourceLabelId) || connected.has(edge.outputLabelId))
    if (index < 0) return null
    const [edge] = remaining.splice(index, 1)
    connected.add(edge.sourceLabelId)
    connected.add(edge.outputLabelId)
  }
  return { ...(source as unknown as InventoryLabelGenealogy), edges: decoded }
}

export function decodeInventoryLabelMovementTimeline(
  value: unknown,
  expectedLabelId: string,
): InventoryLabelMovementTimeline | null {
  const source = record(value)
  if (source === null || !exact(source, ['labelId', 'entries', 'windowFull']) ||
    source.labelId !== expectedLabelId || !Array.isArray(source.entries) || source.entries.length > 100 ||
    typeof source.windowFull !== 'boolean') return null
  const entries = source.entries.map(movement)
  if (entries.some((entry) => entry === null)) return null
  const decoded = entries as InventoryLabelMovement[]
  if (new Set(decoded.map((entry) => entry.movementId)).size !== decoded.length ||
    decoded.some((entry, index) => index > 0 && Date.parse(decoded[index - 1].occurredAtUtc) < Date.parse(entry.occurredAtUtc))) return null
  return { ...(source as unknown as InventoryLabelMovementTimeline), entries: decoded }
}

export function combineInventoryLabelDetail(
  labelVersion: string,
  identity: InventoryLabelIdentity,
  balances: InventoryLabelBalances,
  genealogy: InventoryLabelGenealogy,
  movementTimeline: InventoryLabelMovementTimeline,
  expectedCompanyId: string,
  expectedLabelId: string,
): InventoryLabelDetailWithMovementTimeline | null {
  if (!version(labelVersion) ||
    identity.companyId !== expectedCompanyId || identity.labelId !== expectedLabelId ||
    balances.labelId !== expectedLabelId || genealogy.labelId !== expectedLabelId ||
    movementTimeline.labelId !== expectedLabelId ||
    genealogy.labelCode !== identity.code ||
    balances.positions.some((item) => item.productId !== identity.productId) ||
    movementTimeline.entries.some((entry) => entry.lines.some((line) => line.productId !== identity.productId))) return null
  return { labelVersion, identity, balances, genealogy, movementTimeline }
}

export function decodeInventoryLabelDetail(
  value: unknown,
  expectedCompanyId: string,
  expectedLabelId: string,
): InventoryLabelDetailWithMovementTimeline | null {
  const source = record(value)
  if (source === null || !exact(source, ['labelVersion', 'identity', 'balances', 'genealogy', 'movementTimeline']) ||
    typeof source.labelVersion !== 'string') return null
  const identity = decodeInventoryLabelIdentity(source.identity)
  const balances = decodeInventoryLabelBalances(source.balances)
  const genealogy = identity === null
    ? null
    : decodeInventoryLabelGenealogy(source.genealogy, expectedLabelId, identity.code)
  const movementTimeline = decodeInventoryLabelMovementTimeline(source.movementTimeline, expectedLabelId)
  return identity === null || balances === null || genealogy === null || movementTimeline === null
    ? null
    : combineInventoryLabelDetail(
      source.labelVersion,
      identity,
      balances,
      genealogy,
      movementTimeline,
      expectedCompanyId,
      expectedLabelId,
    )
}

/**
 * The #597 stock-position response in its OPT-IN shape (`X-ERP-Inventory-Stock-Display: 1.0`), decoded strictly.
 *
 * Source: contract pin d821e6b8 (`InventoryStockPositionPageV1` / `InventoryStockPositionDisplayV1` /
 * `ProductStockDisplayResultV1`) and the coordinator consumer handoff STOK-UX-d821e6b8 r7. The previously published
 * response keeps its own strict decoder in `./contract` (unchanged): the two shapes overlap when empty, so a
 * response is decoded by the mode that was requested, never by guessing its shape.
 *
 * Fails closed (null) on any unknown or missing key, a broken quantity identity, or a Product display that is not
 * bound to the row's exact Product and version. Nothing missing is filled in: an absent identity or technical
 * block stays null, and its `errors` are kept for the owner, never shown as text.
 */

export type StockClassification = 'Standard' | 'SecondQuality' | 'Scrap'
export type StockPartyKind = 'Company' | 'Counterparty'

export const STOCK_CLASSIFICATIONS: readonly StockClassification[] = ['Standard', 'SecondQuality', 'Scrap']
export const STOCK_PARTY_KINDS: readonly StockPartyKind[] = ['Company', 'Counterparty']

export interface StockProductIdentity {
  readonly productId: string
  readonly productVersion: string
  readonly code: string
  readonly name: string
}

export interface StockProductTechnical {
  readonly family: { readonly familyId: string; readonly familyCode: string; readonly sourceRevision: string }
  readonly template: {
    readonly templateId: string
    readonly templateVersion: string
    readonly templateCode: string
    readonly sourceRevision: string
  }
  /** Canonical decimal text: the wire sends a JSON number or string; it is never re-rounded here. */
  readonly thicknessMm: string
  readonly widthMm: string
}

export interface StockProductDisplay {
  readonly productId: string
  readonly expectedProductVersion: string
  readonly identity: StockProductIdentity | null
  readonly technical: StockProductTechnical | null
  readonly errors: readonly { readonly field: string; readonly code: string }[]
}

export interface StockDisplayRow {
  readonly companyId: string
  readonly labelId: string
  readonly labelVersion: string
  readonly labelCode: string
  readonly productId: string
  readonly productVersion: string
  readonly classification: StockClassification | null
  readonly lot: { readonly id: string; readonly heatNumber: string | null; readonly coilNumber: string | null }
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly documentStatus: string
  readonly ownerKind: StockPartyKind
  readonly ownerId: string
  readonly custodianKind: StockPartyKind
  readonly custodianId: string
  readonly locationId: string
  /** #588: Fiziksel. */
  readonly physicalKg: string
  /** #588: Blokajsız fiziksel — `physicalKg − blockedPhysicalKg`. */
  readonly freePhysicalKg: string
  /** #588: Bloklu. */
  readonly blockedPhysicalKg: string
  /** #588: Tahsisli. */
  readonly activeAllocationKg: string
  /** #588: Kullanılabilir — `freePhysicalKg − activeAllocationKg`. */
  readonly availableKg: string
  readonly productDisplay: StockProductDisplay
}

export interface StockDisplayPage {
  readonly items: readonly StockDisplayRow[]
  readonly searchWindowFull: boolean
  /** The opaque forward continuation; null when the server has nothing after this page. */
  readonly nextCursor: string | null
}

/**
 * STOCK-LOCATION-2: the Organization owner's display of the row's own location, as the #607 producer publishes it in
 * the `2.0` response (`OrganizationLocationDisplayV1`). Bound to the row's `locationId`; never looked up or guessed here.
 */
export interface StockLocationDisplay {
  readonly locationId: string
  readonly locationCode: string
  readonly locationName: string
}

/** A `2.0` row: every `1.0` member unchanged, plus the required, non-null location display. */
export interface StockLocationDisplayRow extends StockDisplayRow {
  readonly locationDisplay: StockLocationDisplay
}

export interface StockLocationDisplayPage {
  readonly items: readonly StockLocationDisplayRow[]
  readonly searchWindowFull: boolean
  readonly nextCursor: string | null
}

type JsonObject = Record<string, unknown>

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const VERSION = /^[1-9]\d*$/
/** Canonical non-negative kilograms: no sign, no leading or trailing zeros, at most three fraction digits. */
const KG = /^(0|[1-9]\d*)(\.\d{0,2}[1-9])?$/
/** The OpenAPI pattern of the nominal millimetre fields. */
const MM = /^-?(?:0|[1-9]\d*)(?:\.\d+)?$/
const MAX_INT64 = 9223372036854775807n
const MAX_CURSOR_LENGTH = 4096

const PAGE_KEYS = ['items', 'searchWindowFull', 'nextCursor'] as const
const ROW_KEYS = [
  'companyId', 'labelId', 'labelVersion', 'labelCode', 'productId', 'productVersion', 'classification', 'lot',
  'heatNumber', 'coilNumber', 'documentStatus', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId', 'locationId',
  'physicalKg', 'freePhysicalKg', 'blockedPhysicalKg', 'activeAllocationKg', 'availableKg', 'productDisplay',
] as const

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function nullableText(value: unknown): value is string | null {
  return value === null || text(value)
}

function guid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID
}

function version(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 19 && VERSION.test(value) && BigInt(value) <= MAX_INT64
}

function kg(value: unknown): value is string {
  return typeof value === 'string' && KG.test(value)
}

function thousandths(value: string): bigint {
  const [whole, fraction = ''] = value.split('.')
  return BigInt(whole) * 1000n + BigInt(fraction.padEnd(3, '0'))
}

function oneOf<T extends string>(value: unknown, allowed: readonly T[]): value is T {
  return typeof value === 'string' && (allowed as readonly string[]).includes(value)
}

/** A nominal millimetre value as decimal text, or null. A JSON number must already print in the pattern. */
function millimetres(value: unknown): string | null {
  if (typeof value === 'string') return MM.test(value) ? value : null
  if (typeof value !== 'number' || !Number.isFinite(value)) return null
  const printed = String(value)
  return MM.test(printed) ? printed : null
}

function identityOf(value: unknown, productId: string, expectedVersion: string): StockProductIdentity | null | undefined {
  if (value === null) return null
  const source = record(value)
  if (source === null || !exact(source, ['productId', 'productVersion', 'code', 'name']) ||
    source.productId !== productId || source.productVersion !== expectedVersion ||
    !text(source.code) || !text(source.name)) return undefined
  return { productId, productVersion: expectedVersion, code: source.code, name: source.name }
}

function technicalOf(value: unknown): StockProductTechnical | null | undefined {
  if (value === null) return null
  const source = record(value)
  if (source === null || !exact(source, ['family', 'template', 'thicknessMm', 'widthMm'])) return undefined
  const family = record(source.family)
  const template = record(source.template)
  const thicknessMm = millimetres(source.thicknessMm)
  const widthMm = millimetres(source.widthMm)
  if (family === null || !exact(family, ['familyId', 'familyCode', 'sourceRevision']) || !guid(family.familyId) ||
    !text(family.familyCode) || !text(family.sourceRevision)) return undefined
  if (template === null || !exact(template, ['templateId', 'templateVersion', 'templateCode', 'sourceRevision']) ||
    !guid(template.templateId) || !text(template.templateVersion) || !text(template.templateCode) ||
    !text(template.sourceRevision)) return undefined
  if (thicknessMm === null || widthMm === null) return undefined
  return {
    family: { familyId: family.familyId, familyCode: family.familyCode, sourceRevision: family.sourceRevision },
    template: {
      templateId: template.templateId,
      templateVersion: template.templateVersion,
      templateCode: template.templateCode,
      sourceRevision: template.sourceRevision,
    },
    thicknessMm,
    widthMm,
  }
}

function productDisplayOf(value: unknown, productId: string, productVersion: string): StockProductDisplay | null {
  const source = record(value)
  if (source === null || !exact(source, ['productId', 'expectedProductVersion', 'identity', 'technical', 'errors']) ||
    source.productId !== productId || source.expectedProductVersion !== productVersion ||
    !Array.isArray(source.errors)) return null
  const identity = identityOf(source.identity, productId, productVersion)
  const technical = technicalOf(source.technical)
  if (identity === undefined || technical === undefined) return null
  const errors: { readonly field: string; readonly code: string }[] = []
  for (const entry of source.errors) {
    const error = record(entry)
    if (error === null || !exact(error, ['field', 'code']) || !text(error.field) || !text(error.code)) return null
    errors.push({ field: error.field, code: error.code })
  }
  return { productId, expectedProductVersion: productVersion, identity, technical, errors }
}

/** Exactly the three members, bound to the row's own location; a blank code or name is not a display. */
function locationDisplayOf(value: unknown, locationId: string): StockLocationDisplay | null {
  const source = record(value)
  if (source === null || !exact(source, ['locationId', 'locationCode', 'locationName']) ||
    !guid(source.locationId) || source.locationId.toLowerCase() !== locationId.toLowerCase() ||
    !text(source.locationCode) || !text(source.locationName)) return null
  return { locationId: source.locationId, locationCode: source.locationCode, locationName: source.locationName }
}

function rowOf(value: unknown, keys: readonly string[] = ROW_KEYS): StockDisplayRow | null {
  const source = record(value)
  if (source === null || !exact(source, keys)) return null
  const {
    companyId, labelId, labelVersion, labelCode, productId, productVersion, classification, heatNumber, coilNumber,
    documentStatus, ownerKind, ownerId, custodianKind, custodianId, locationId,
    physicalKg, freePhysicalKg, blockedPhysicalKg, activeAllocationKg, availableKg,
  } = source
  // A non-empty opt-in row always carries the label's stored Product version: the host answers 503 for a null or
  // blank one. Any other text is passed to the Product owner, which answers a version it cannot read with its own
  // per-row result (identity and technical null, `invalid_version`) — so the row is kept and shown as "—" rather
  // than refusing the page. The Product display is bound to that exact text either way.
  if (!guid(companyId) || !guid(labelId) || !version(labelVersion) || !text(labelCode) || !guid(productId) ||
    !text(productVersion) || !(classification === null || oneOf(classification, STOCK_CLASSIFICATIONS)) ||
    !nullableText(heatNumber) || !nullableText(coilNumber) || !text(documentStatus) ||
    !oneOf(ownerKind, STOCK_PARTY_KINDS) || !guid(ownerId) || !oneOf(custodianKind, STOCK_PARTY_KINDS) ||
    !guid(custodianId) || !guid(locationId) || !kg(physicalKg) || !kg(freePhysicalKg) ||
    !kg(blockedPhysicalKg) || !kg(activeAllocationKg) || !kg(availableKg)) return null
  const lot = record(source.lot)
  if (lot === null || !exact(lot, ['id', 'heatNumber', 'coilNumber']) || !guid(lot.id) ||
    !nullableText(lot.heatNumber) || !nullableText(lot.coilNumber)) return null
  // #588: physical = blocked + free-physical; free-physical = allocated + available. Exact, in thousandths.
  if (thousandths(physicalKg) !== thousandths(blockedPhysicalKg) + thousandths(freePhysicalKg) ||
    thousandths(freePhysicalKg) !== thousandths(activeAllocationKg) + thousandths(availableKg)) return null
  const productDisplay = productDisplayOf(source.productDisplay, productId, productVersion)
  if (productDisplay === null) return null
  return {
    companyId, labelId, labelVersion, labelCode, productId, productVersion, classification,
    lot: { id: lot.id, heatNumber: lot.heatNumber, coilNumber: lot.coilNumber },
    heatNumber, coilNumber, documentStatus, ownerKind, ownerId, custodianKind, custodianId, locationId,
    physicalKg, freePhysicalKg, blockedPhysicalKg, activeAllocationKg, availableKg, productDisplay,
  }
}

function pageOf<Row extends StockDisplayRow>(value: unknown, decodeRow: (row: unknown) => Row | null) {
  const source = record(value)
  // `nextCursor` is required-nullable at runtime but optional in the published schema: absent reads as null.
  if (source === null || Object.keys(source).some((key) => !(PAGE_KEYS as readonly string[]).includes(key)) ||
    !Array.isArray(source.items) || typeof source.searchWindowFull !== 'boolean') return null
  const cursor = source.nextCursor ?? null
  if (cursor !== null && (typeof cursor !== 'string' || cursor.length < 1 || cursor.length > MAX_CURSOR_LENGTH)) return null
  const items: Row[] = []
  for (const entry of source.items) {
    const row = decodeRow(entry)
    if (row === null) return null
    items.push(row)
  }
  if (new Set(items.map((item) => item.labelId)).size !== items.length) return null
  return { items, searchWindowFull: source.searchWindowFull, nextCursor: cursor as string | null }
}

/** The `1.0` opt-in page, or null when anything in it is outside the published contract. Kept for `1.0` readers. */
export function decodeStockDisplayPage(value: unknown): StockDisplayPage | null {
  return pageOf(value, (row) => rowOf(row))
}

const LOCATION_ROW_KEYS = [...ROW_KEYS, 'locationDisplay'] as const

function locationRowOf(value: unknown): StockLocationDisplayRow | null {
  const row = rowOf(value, LOCATION_ROW_KEYS)
  if (row === null) return null
  const locationDisplay = locationDisplayOf((value as JsonObject).locationDisplay, row.locationId)
  return locationDisplay === null ? null : { ...row, locationDisplay }
}

/**
 * The `2.0` (Product + Location) opt-in page (#607): the `1.0` rules unchanged for every row, plus a required
 * `locationDisplay` bound to the row's `locationId`. A missing, null, mistyped, blank, foreign or extra member refuses
 * the whole page; the `1.0` decoder above is never used for a `2.0` answer.
 */
export function decodeStockLocationDisplayPage(value: unknown): StockLocationDisplayPage | null {
  return pageOf(value, locationRowOf)
}

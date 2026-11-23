export const PHYSICAL_ALLOCATIONS_PATH = '/inventory/physical-allocations' as const
export const PHYSICAL_ALLOCATION_SUGGESTIONS_PATH = '/inventory/physical-allocations/suggestions' as const

export type AllocationStatus = 'Active' | 'Consumed' | 'Released'

/**
 * Declared 422 `x-erp-problem-codes` of `POST /api/v1/inventory/physical-allocations`
 * (contracts/openapi/goymen-erp-v1.json). The UI branches on the code, never on server text.
 */
export const CREATE_ALLOCATION_422_CODES = {
  invalidReference: 'inventory_allocation_invalid_reference',
  ownerNotCompany: 'inventory_allocation_owner_not_company',
  /** KLN-D39/D70: the confirmed "muadil" label's product is not similar within the Settings tolerance (or none is set). */
  substituteNotSimilar: 'inventory_allocation_substitute_not_similar',
  /** KLN-D66 (3): the kilogram comparison with the Sales order unit has no conversion source; refused, never guessed. */
  unitConversionUnavailable: 'inventory_allocation_unit_conversion_unavailable',
  /** KLN-D119: the label's free stock tracks pieces; the allocation must name how many it reserves. */
  pieceCountRequired: 'inventory_allocation_piece_count_required',
  /** KLN-D119: the label's free stock tracks no pieces; a piece count is refused, never dropped. */
  pieceCountNotTracked: 'inventory_allocation_piece_count_not_tracked',
  /** KLN-D119: the piece count is not a whole number of at least one, or does not fit the reserved kilograms. */
  pieceCountInvalid: 'inventory_allocation_piece_count_invalid',
} as const

/** Declared 409 code of the create command added by KLN-D119: fewer unreserved pieces than asked. */
export const CREATE_ALLOCATION_INSUFFICIENT_FREE_PIECES = 'inventory_allocation_insufficient_free_pieces' as const

export interface PhysicalAllocation {
  readonly allocationId: string
  readonly companyId: string
  readonly version: string
  readonly status: AllocationStatus
  readonly labelId: string
  readonly productId: string
  readonly demandKind: 'sales.order-line'
  readonly demandId: string
  readonly demandLineId: string
  readonly demandProductVersion: string
  readonly allocatedKg: string
  readonly remainingKg: string
  readonly reason: string
  readonly reservedUntilUtc: string | null
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  /** KLN-D39: the allocation reserves a label of a similar product ("muadil"); `productId` stays the line's product. */
  readonly isSubstitute: boolean
  /** KLN-D119: the pieces the allocation reserved; absent when it holds none (a kg-only label). */
  readonly allocatedPieces?: number
  /** KLN-D119: the pieces still reserved; present exactly when `allocatedPieces` is. */
  readonly remainingPieces?: number
}

export interface PhysicalAllocationItem {
  readonly allocation: PhysicalAllocation
  readonly labelVersion: string
}

export interface PhysicalAllocationPage {
  readonly items: readonly PhysicalAllocationItem[]
  readonly searchWindowFull: boolean
}

export interface AllocationScope {
  readonly companyId: string
  readonly salesOrderId: string
  readonly salesOrderLineId: string
  readonly productId: string
  readonly productVersion: string
}

export interface CreatePhysicalAllocationBody {
  readonly allocationId: string
  readonly salesOrderId: string
  readonly salesOrderLineId: string
  readonly labelId: string
  readonly expectedLabelVersion: string
  readonly quantityKg: string
  readonly reason: string
  readonly reservedUntilUtc: string | null
  /** Sent only for a confirmed similar-product suggestion; the server re-checks the similarity. */
  readonly isSubstitute?: true
  /** KLN-D119: sent only when the label tracks pieces (or the user entered one); the server decides. */
  readonly pieceCount?: number
}

export interface ReleasePhysicalAllocationBody {
  readonly expectedAllocationVersion: string
  readonly reason: string
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const VERSION = /^[1-9][0-9]*$/
const DECIMAL = /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/
const STATUS = new Set<AllocationStatus>(['Active', 'Consumed', 'Released'])

const record = (value: unknown): Record<string, unknown> | null =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null

/** Exact key set: `keys` (sorted) must all be present, `optional` may be absent, nothing else is accepted. */
const exactKeys = (source: Record<string, unknown>, keys: readonly string[], optional: readonly string[] = []) => {
  const actual = Object.keys(source).filter((key) => !optional.includes(key)).sort()
  return actual.length === keys.length && actual.every((key, index) => key === keys[index])
}
/** `InventoryPhysicalAllocationViewV1`: required members (sorted) and the members the server may omit. */
const ALLOCATION_KEYS = ['allocatedKg', 'allocationId', 'companyId', 'createdAtUtc', 'demandId', 'demandKind', 'demandLineId',
  'demandProductVersion', 'labelId', 'productId', 'reason', 'remainingKg', 'reservedUntilUtc', 'status', 'updatedAtUtc', 'version']
const ALLOCATION_OPTIONAL_KEYS = ['allocatedPieces', 'isSubstitute', 'remainingPieces']
const ALLOCATION_PAGE_KEYS = ['items', 'searchWindowFull']
const ALLOCATION_ITEM_KEYS = ['allocation', 'labelVersion']

const text = (value: unknown): value is string => typeof value === 'string' && value.trim() === value && value.length > 0
const guid = (value: unknown): value is string => text(value) && GUID.test(value) && value.toLowerCase() !== NIL_GUID
const version = (value: unknown): value is string => text(value) && VERSION.test(value)
const decimal = (value: unknown): value is string => text(value) && DECIMAL.test(value)
const instant = (value: unknown): value is string => text(value) && !Number.isNaN(Date.parse(value))
const INT32_MAX = 2147483647
/** An optional piece member: absent is "no pieces"; present must be a whole number (never null, text or a fraction). */
const optionalPieces = (value: unknown): value is number | undefined => value === undefined
  || (typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= INT32_MAX)

/**
 * The allocation form's piece count as typed: a whole number of at least one (no sign, no leading zero, within int32);
 * anything else is `null` and nothing is sent.
 */
export function parseAllocationPieceCount(value: string): number | null {
  if (!/^[1-9][0-9]{0,9}$/.test(value)) return null
  const parsed = Number(value)
  return parsed <= INT32_MAX ? parsed : null
}

export function decodePhysicalAllocation(value: unknown, expected: AllocationScope): PhysicalAllocation | null {
  const source = record(value)
  if (source === null || !exactKeys(source, ALLOCATION_KEYS, ALLOCATION_OPTIONAL_KEYS)
    || !guid(source.allocationId) || !guid(source.companyId) || !version(source.version)
    || typeof source.status !== 'string' || !STATUS.has(source.status as AllocationStatus)
    || !guid(source.labelId) || !guid(source.productId) || source.demandKind !== 'sales.order-line'
    || !guid(source.demandId) || !guid(source.demandLineId) || !text(source.demandProductVersion)
    || !decimal(source.allocatedKg) || !decimal(source.remainingKg) || !text(source.reason)
    || !(source.reservedUntilUtc === null || instant(source.reservedUntilUtc))
    || !instant(source.createdAtUtc) || !instant(source.updatedAtUtc)
    || !(source.isSubstitute === undefined || typeof source.isSubstitute === 'boolean')
    || !optionalPieces(source.allocatedPieces) || !optionalPieces(source.remainingPieces)) return null
  // KLN-D119: both piece figures or neither; the reserved pieces never exceed the allocated ones.
  if ((source.allocatedPieces === undefined) !== (source.remainingPieces === undefined)
    || (source.allocatedPieces !== undefined && source.remainingPieces! > source.allocatedPieces)) return null

  if (source.companyId !== expected.companyId || source.demandId !== expected.salesOrderId
    || source.demandLineId !== expected.salesOrderLineId || source.productId !== expected.productId
    || source.demandProductVersion !== expected.productVersion) return null
  if (source.status === 'Active' ? source.remainingKg === '0' : source.remainingKg !== '0') return null

  return { ...(source as unknown as PhysicalAllocation), isSubstitute: source.isSubstitute === true }
}

export function decodePhysicalAllocationPage(value: unknown, expected: AllocationScope): PhysicalAllocationPage | null {
  const source = record(value)
  if (source === null || !exactKeys(source, ALLOCATION_PAGE_KEYS) || !Array.isArray(source.items) || typeof source.searchWindowFull !== 'boolean') return null
  const items: PhysicalAllocationItem[] = []
  const ids = new Set<string>()
  for (const raw of source.items) {
    const item = record(raw)
    const allocation = item === null || !exactKeys(item, ALLOCATION_ITEM_KEYS) ? null : decodePhysicalAllocation(item.allocation, expected)
    if (item === null || allocation === null || !version(item.labelVersion) || ids.has(allocation.allocationId)) return null
    ids.add(allocation.allocationId)
    items.push({ allocation, labelVersion: item.labelVersion })
  }
  return { items, searchWindowFull: source.searchWindowFull }
}

export function problemCode(value: unknown): string | null {
  const source = record(value)
  return source !== null && text(source.code) ? source.code : null
}

export function isGuid(value: string): boolean {
  return guid(value)
}

export function isPositiveVersion(value: string): boolean {
  return VERSION.test(value)
}

export type AllocationSuggestionKind = 'same-product' | 'similar-product'
export type SimilarProductMode = 'same-product-only' | 'dimension-tolerance'

export interface AllocationSuggestion {
  readonly labelId: string
  readonly labelVersion: string
  readonly labelCode: string
  readonly productId: string
  readonly productVersion: string | null
  readonly productCode: string | null
  readonly productName: string | null
  readonly kind: AllocationSuggestionKind
  readonly isSubstitute: boolean
  readonly unreservedKg: string
  readonly maximumDeviationPercent: string | null
  /** KLN-D119: the label's unreserved pieces; absent for a kg-only label. Present means the create asks a piece count. */
  readonly unreservedPieces?: number
}

export interface AllocationSuggestions {
  readonly similarProductMode: SimilarProductMode
  readonly aiSuggestions: 'disabled' | 'enabled'
  readonly truncated: boolean
  readonly items: readonly AllocationSuggestion[]
  /**
   * KLN-D99: the requested kilograms the server ordered the line's labels by (closest first, ties by label code), in
   * its normalized form; `null` when no quantity was sent. The list stays a suggestion: the user picks by hand.
   */
  readonly requestedKg: string | null
}

const REQUESTED_KG = /^[0-9]+(?:\.[0-9]{1,3})?$/
const REQUESTED_KG_MAX_LENGTH = 32

/**
 * KLN-D99: the allocation form's kilograms as the suggestion query's `requestedKg`, normalized exactly like the server
 * echoes it (no leading zeros, no trailing fraction zeros). Empty, zero, a comma, more than three decimals or any other
 * text gives `null`: the list is then read without a proximity order, never with a rounded quantity.
 */
export function normalizeRequestedKg(value: string): string | null {
  if (value.length > REQUESTED_KG_MAX_LENGTH || !REQUESTED_KG.test(value)) return null
  const [whole, fraction = ''] = value.split('.')
  const integer = whole.replace(/^0+(?=[0-9])/, '')
  const decimals = fraction.replace(/0+$/, '')
  if (integer === '0' && decimals === '') return null
  return decimals === '' ? integer : `${integer}.${decimals}`
}

const SUGGESTIONS_KEYS = ['aiSuggestions', 'companyId', 'items', 'productId', 'productVersion', 'requestedKg',
  'salesOrderId', 'salesOrderLineId', 'similarProductMode', 'truncated']
const SUGGESTION_KEYS = ['isSubstitute', 'kind', 'labelCode', 'labelId', 'labelVersion', 'maximumDeviationPercent',
  'productCode', 'productId', 'productName', 'productVersion', 'unreservedKg']
const POSITIVE_DECIMAL = (value: unknown): value is string => decimal(value) && !/^0(?:\.0+)?$/.test(value)
const optionalText = (value: unknown): value is string | null => value === null || text(value)
const SUGGESTION_OPTIONAL_KEYS = ['unreservedPieces']

function decodeSuggestion(value: unknown, expected: AllocationScope, mode: SimilarProductMode): AllocationSuggestion | null {
  const source = record(value)
  if (source === null || !exactKeys(source, SUGGESTION_KEYS, SUGGESTION_OPTIONAL_KEYS)
    || !optionalPieces(source.unreservedPieces) || !guid(source.labelId) || !version(source.labelVersion)
    || !text(source.labelCode) || !guid(source.productId) || !optionalText(source.productVersion)
    || !optionalText(source.productCode) || !optionalText(source.productName)
    || typeof source.isSubstitute !== 'boolean' || !POSITIVE_DECIMAL(source.unreservedKg)
    || !(source.maximumDeviationPercent === null || decimal(source.maximumDeviationPercent))) return null
  const sameProduct = source.productId === expected.productId
  if (source.kind === 'same-product') {
    // The line's own product: never a substitute and never a deviation.
    if (!sameProduct || source.isSubstitute || source.maximumDeviationPercent !== null) return null
  } else if (source.kind === 'similar-product') {
    // KLN-D39/D40: another product, flagged "muadil", only when Settings configured a tolerance.
    if (sameProduct || !source.isSubstitute || source.maximumDeviationPercent === null
      || mode !== 'dimension-tolerance' || source.productVersion === null) return null
  } else {
    return null
  }
  return source as unknown as AllocationSuggestion
}

/**
 * Strict decoder of `GET /inventory/physical-allocations/suggestions` (KLN-D34/D39/D40). The answer must be for the
 * requested company, order line and the line's exact product version; one torn item rejects the whole list.
 */
export function decodeAllocationSuggestions(
  value: unknown,
  expected: AllocationScope,
  expectedRequestedKg: string | null = null,
): AllocationSuggestions | null {
  const source = record(value)
  // KLN-D99: the answer must echo the quantity it was ordered by; another quantity means another ordering.
  if (source === null || !exactKeys(source, SUGGESTIONS_KEYS) || source.requestedKg !== expectedRequestedKg
    || source.companyId !== expected.companyId
    || source.salesOrderId !== expected.salesOrderId || source.salesOrderLineId !== expected.salesOrderLineId
    || source.productId !== expected.productId || source.productVersion !== expected.productVersion
    || (source.similarProductMode !== 'same-product-only' && source.similarProductMode !== 'dimension-tolerance')
    || (source.aiSuggestions !== 'disabled' && source.aiSuggestions !== 'enabled')
    || typeof source.truncated !== 'boolean' || !Array.isArray(source.items)) return null
  const mode = source.similarProductMode
  const items: AllocationSuggestion[] = []
  const labels = new Set<string>()
  for (const raw of source.items) {
    const item = decodeSuggestion(raw, expected, mode)
    if (item === null || labels.has(item.labelId.toLowerCase())) return null
    labels.add(item.labelId.toLowerCase())
    items.push(item)
  }
  // KLN-D39: similar products are offered only when the line's own product has no label.
  const kinds = new Set(items.map((item) => item.kind))
  if (kinds.size > 1) return null
  // The server's order is kept as sent: the proximity order (KLN-D99) is the backend's, never re-sorted here.
  return {
    similarProductMode: mode,
    aiSuggestions: source.aiSuggestions,
    truncated: source.truncated,
    items,
    requestedKg: expectedRequestedKg,
  }
}

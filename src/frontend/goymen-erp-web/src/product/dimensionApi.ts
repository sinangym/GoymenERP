/**
 * BS-9 (2026-11-11) — the "Ebat/kalınlık" row of a product, ONE row on the shared product card (the same in every company; a
 * holder in any company changes it), pinned to `GET/PUT /api/v1/product-dimensions/{productId}` (`ProductDimensionEndpoints`).
 * The U-08 unit conversion reads its input from this row; a product without one has "kaynak yok". Reads send the selected
 * company only; the command carries one caller-owned `Idempotency-Key` (kept for a retry after an unknown outcome). Success is
 * exactly the contract's status; a 5xx or an off-contract answer is an UNKNOWN outcome; a refusal is read by its status and
 * `code`. The measures are never rounded here: the server refuses a value with more decimals than its column holds (URN-D8).
 * URN-D16 (STK-S37R): the kg/m is never typed on the card; the read shows the acting company's current imported table value with
 * its source label and table version, and a profile whose measures the table does not hold is refused (409).
 */

export const PRODUCT_SECTION_KINDS = ['Sheet', 'Coil', 'RoundTube', 'Profile'] as const
export type ProductSectionKind = (typeof PRODUCT_SECTION_KINDS)[number]

export const DIMENSION_MEASURES = [
  'thicknessMm',
  'widthMm',
  'lengthMm',
  'heightMm',
  'outerDiameterMm',
  'wallThicknessMm',
] as const
export type DimensionMeasure = (typeof DIMENSION_MEASURES)[number]

export type MeasurePresence = 'required' | 'optional' | 'forbidden'

/**
 * Which millimetre measures a section kind holds — the same rule the server and the database check (`ProductDimensionShape`,
 * `CK_ProductCardDimensions_Shape`). The kg/m is not among them: it comes from the company's table (`SECTION_TABLE_WEIGHT`).
 */
export const SECTION_MEASURES: Record<ProductSectionKind, Record<DimensionMeasure, MeasurePresence>> = {
  Sheet: {
    thicknessMm: 'required', widthMm: 'required', lengthMm: 'required', heightMm: 'forbidden',
    outerDiameterMm: 'forbidden', wallThicknessMm: 'forbidden',
  },
  Coil: {
    thicknessMm: 'required', widthMm: 'required', lengthMm: 'forbidden', heightMm: 'forbidden',
    outerDiameterMm: 'forbidden', wallThicknessMm: 'forbidden',
  },
  RoundTube: {
    thicknessMm: 'forbidden', widthMm: 'forbidden', lengthMm: 'forbidden', heightMm: 'forbidden',
    outerDiameterMm: 'required', wallThicknessMm: 'required',
  },
  Profile: {
    thicknessMm: 'forbidden', widthMm: 'required', lengthMm: 'forbidden', heightMm: 'required',
    outerDiameterMm: 'forbidden', wallThicknessMm: 'required',
  },
}

/**
 * Whether a kind's kg/m comes from the company's imported table (URN-D16): a profile cannot be saved without a matching table
 * row, a round tube's table value is optional, a sheet or a coil has none.
 */
export const SECTION_TABLE_WEIGHT: Record<ProductSectionKind, 'required' | 'optional' | 'none'> = {
  Sheet: 'none',
  Coil: 'none',
  RoundTube: 'optional',
  Profile: 'required',
}

/** The decimals each millimetre measure may carry (`ProductDimensionInputLimitsV1`). */
export const MEASURE_DECIMALS: Record<DimensionMeasure, number> = {
  thicknessMm: 3, widthMm: 3, lengthMm: 3, heightMm: 3, outerDiameterMm: 3, wallThicknessMm: 3,
}

export const DIMENSION_PROBLEM = {
  versionConflict: 'product_dimension_version_conflict',
  reasonRequired: 'product_dimension_reason_required',
  measureInvalid: 'product_dimension_measure_invalid',
  idempotencyConflict: 'product_dimension_idempotency_conflict',
  /** URN-D16: the company's current kg/m table does not hold this profile's width, height and wall. */
  weightSourceMissing: 'product_dimension_weight_source_missing',
} as const

export type DimensionMeasures = Readonly<Record<DimensionMeasure, number | null>>

/** The kg/m the acting company's current table holds for the stored measures, with its mandatory source (URN-D14). */
export interface TableWeight {
  readonly kgPerMetre: number
  readonly sourceLabel: string
  readonly tableVersion: string
}

export interface ProductDimensionView {
  readonly productId: string
  readonly version: string
  readonly sectionKind: ProductSectionKind | null
  readonly measures: DimensionMeasures
  /** Null when the table holds no row for these measures (or the kind takes none). */
  readonly tableWeight: TableWeight | null
  /** The value is shared: the last change was made in another company. */
  readonly changedInAnotherCompany: boolean
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export type DimensionRead =
  | { readonly type: 'ok'; readonly data: ProductDimensionView }
  /** 401: the session ended — not the same as lacking the permission. */
  | { readonly type: 'unauthenticated' }
  /** 403: no `product.dimension.manage` here. */
  | { readonly type: 'forbidden' }
  /** 404: the product card is not there. */
  | { readonly type: 'not-found' }
  /** 503: the provider is not wired in this host. */
  | { readonly type: 'unavailable' }
  | { readonly type: 'failed' }

export type DimensionCommand =
  | { readonly type: 'applied' }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown' }

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return { ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}), 'X-Company-Id': companyId }
}

export function dimensionPath(productId: string): string {
  return `/product-dimensions/${encodeURIComponent(productId)}`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** Exactly these members, no more and no fewer: an unknown member is off-contract. */
function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

/** `ProductDimensionViewV1`'s members. */
const DIMENSION_VIEW_KEYS = [
  'companyId', 'productId', 'version', 'sectionKind', ...DIMENSION_MEASURES, 'kgPerMetre', 'updatedAtUtc', 'updatedBy',
  'updatedInCompanyId', 'kgPerMetreSourceLabel', 'kgPerMetreTableVersion',
] as const

function isPositiveNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

/** True when the measures follow the kind's shape: required ones present, forbidden ones null. */
export function fitsSection(kind: ProductSectionKind, measures: Readonly<Record<DimensionMeasure, unknown>>): boolean {
  return DIMENSION_MEASURES.every((measure) => {
    const presence = SECTION_MEASURES[kind][measure]
    const value = measures[measure]
    if (value === null) return presence !== 'required'
    return presence !== 'forbidden' && isPositiveNumber(value)
  })
}

export function decodeDimension(raw: unknown, companyId: string, productId: string): ProductDimensionView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, DIMENSION_VIEW_KEYS) || raw.companyId !== companyId || raw.productId !== productId
    || typeof raw.version !== 'string' || !/^(0|[1-9]\d*)$/.test(raw.version)) {
    return null
  }
  if (!(raw.updatedAtUtc === null || typeof raw.updatedAtUtc === 'string')
    || !(raw.updatedBy === null || typeof raw.updatedBy === 'string')) return null
  const kind = raw.sectionKind
  if (kind !== null && !PRODUCT_SECTION_KINDS.includes(kind as ProductSectionKind)) return null
  if ((raw.version === '0') !== (kind === null)) return null
  const measures = Object.fromEntries(DIMENSION_MEASURES.map((measure) => [measure, raw[measure]])) as Record<DimensionMeasure, unknown>
  if (kind === null ? DIMENSION_MEASURES.some((measure) => measures[measure] !== null) : !fitsSection(kind as ProductSectionKind, measures)) {
    return null
  }
  const updatedIn = raw.updatedInCompanyId
  if (kind === null ? updatedIn !== null : typeof updatedIn !== 'string' || !UUID.test(updatedIn)) return null
  const tableWeight = decodeTableWeight(raw.kgPerMetre, raw.kgPerMetreSourceLabel, raw.kgPerMetreTableVersion)
  if (tableWeight === undefined) return null
  // The table value exists only for the kinds it serves; a later import may drop a row, so a stored profile can read none.
  if (tableWeight !== null && (kind === null || SECTION_TABLE_WEIGHT[kind as ProductSectionKind] === 'none')) return null
  return {
    productId,
    version: raw.version,
    sectionKind: kind as ProductSectionKind | null,
    measures: measures as DimensionMeasures,
    tableWeight,
    changedInAnotherCompany: typeof updatedIn === 'string' && updatedIn.toLowerCase() !== companyId.toLowerCase(),
  }
}

/** All three null (no table value) or all three set; anything else is off-contract (undefined). */
function decodeTableWeight(kgPerMetre: unknown, sourceLabel: unknown, tableVersion: unknown): TableWeight | null | undefined {
  if (kgPerMetre === null && sourceLabel === null && tableVersion === null) return null
  if (!isPositiveNumber(kgPerMetre) || typeof sourceLabel !== 'string' || sourceLabel.trim() === ''
    || typeof tableVersion !== 'string' || !/^[1-9]\d*$/.test(tableVersion)) {
    return undefined
  }
  return { kgPerMetre, sourceLabel, tableVersion }
}

export async function fetchDimension(companyId: string, productId: string, signal?: AbortSignal): Promise<DimensionRead> {
  try {
    const response = await fetch(`${API_BASE}${dimensionPath(productId)}`, { headers: headers(companyId), signal })
    if (response.status === 401) return { type: 'unauthenticated' }
    if (response.status === 403) return { type: 'forbidden' }
    if (response.status === 404) return { type: 'not-found' }
    if (response.status === 503) return { type: 'unavailable' }
    if (response.status !== 200) return { type: 'failed' }
    const data = decodeDimension(await response.json(), companyId, productId)
    return data === null ? { type: 'failed' } : { type: 'ok', data }
  } catch {
    return { type: 'failed' }
  }
}

export interface SetDimensionBody extends DimensionMeasures {
  readonly expectedVersion: string
  readonly sectionKind: ProductSectionKind
  readonly reason: string
}

export async function setDimension(
  companyId: string,
  idempotencyKey: string,
  productId: string,
  body: SetDimensionBody,
): Promise<DimensionCommand> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${dimensionPath(productId)}`, {
      method: 'PUT',
      headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(body),
    })
  } catch {
    return { type: 'unknown' }
  }
  if (response.status === 204) return { type: 'applied' }
  if (response.status >= 500 || response.status < 400) return { type: 'unknown' }
  let raw: unknown = null
  try {
    raw = await response.json()
  } catch {
    raw = null
  }
  const code = isRecord(raw) && typeof raw.code === 'string' && raw.code.length > 0 ? raw.code : null
  return { type: 'refused', status: response.status, code }
}

export type MeasureInput = { readonly ok: true; readonly value: number | null } | { readonly ok: false }

/**
 * One typed measure: empty is null; otherwise a positive decimal (comma or point) with at most the measure's decimals. Never
 * rounded: a value with more decimals is refused here, as the server refuses it.
 */
export function parseMeasure(measure: DimensionMeasure, raw: string): MeasureInput {
  const text = raw.trim().replace(',', '.')
  if (text === '') return { ok: true, value: null }
  const decimals = MEASURE_DECIMALS[measure]
  if (!new RegExp(`^\\d+(\\.\\d{1,${decimals}})?$`).test(text)) return { ok: false }
  const value = Number(text)
  if (!Number.isFinite(value) || value <= 0 || !Number.isSafeInteger(Math.round(value * 10 ** decimals))) return { ok: false }
  return { ok: true, value }
}

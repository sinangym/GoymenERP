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

export const PRODUCT_SECTION_KINDS = [
  'Sheet', 'Coil', 'RoundTube', 'Profile',
  // KLN-D87 (STK-SC33-b): every KLN-D48 section family, in the server's `ProductDimensionSectionKindsV1.All` order.
  'EqualAngle', 'UnequalAngle', 'FlatBar', 'NarrowFlatBar', 'RoundBar', 'SquareBar', 'HexBar', 'TeeBar', 'StructuralProfile',
  'Rebar', 'WireRod', 'WeldedMesh', 'ChequeredSheet', 'PerforatedSheet', 'CorrugatedSheet', 'ErwTube', 'PaintedErwTube',
  'ErwProfile', 'PaintedErwProfile',
] as const
export type ProductSectionKind = (typeof PRODUCT_SECTION_KINDS)[number]

export const DIMENSION_MEASURES = [
  'thicknessMm',
  'widthMm',
  'lengthMm',
  'heightMm',
  'outerDiameterMm',
  'wallThicknessMm',
  /** KLN-D87: the welded mesh spacing (the wire diameter is `outerDiameterMm`). */
  'meshSpacingMm',
] as const
export type DimensionMeasure = (typeof DIMENSION_MEASURES)[number]

/** KLN-D87 text fields: the structural profile series and size designation, the painted ERW surface (paint/colour). */
export const DIMENSION_TEXTS = ['profileSeries', 'profileSize', 'surfaceFinish'] as const
export type DimensionTextField = (typeof DIMENSION_TEXTS)[number]

/** The structural profile series (`ProductProfileSeriesV1`). */
export const PROFILE_SERIES = ['UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM'] as const

/** `ProductDimensionInputLimitsV1`: the size designation and the surface text lengths. */
export const PROFILE_SIZE_MAXIMUM_LENGTH = 16
export const SURFACE_FINISH_MAXIMUM_LENGTH = 200

export type MeasurePresence = 'required' | 'optional' | 'forbidden'

type Shape = Readonly<Record<DimensionMeasure | DimensionTextField, MeasurePresence>>

const R: MeasurePresence = 'required'
const O: MeasurePresence = 'optional'
const F: MeasurePresence = 'forbidden'

/** One row of the server's rule table: thickness, width, length, height, diameter, wall, mesh, series+size, surface. */
function shape(
  thicknessMm: MeasurePresence, widthMm: MeasurePresence, lengthMm: MeasurePresence, heightMm: MeasurePresence,
  outerDiameterMm: MeasurePresence, wallThicknessMm: MeasurePresence, meshSpacingMm: MeasurePresence,
  profile: MeasurePresence, surfaceFinish: MeasurePresence,
): Shape {
  return {
    thicknessMm, widthMm, lengthMm, heightMm, outerDiameterMm, wallThicknessMm, meshSpacingMm,
    profileSeries: profile, profileSize: profile, surfaceFinish,
  }
}

const SHEET = shape(R, R, R, F, F, F, F, F, F)
const TUBE = shape(F, F, F, F, R, R, F, F, F)
const PAINTED_TUBE = shape(F, F, F, F, R, R, F, F, R)
const BOX = shape(F, R, F, R, F, R, F, F, F)
const PAINTED_BOX = shape(F, R, F, R, F, R, F, F, R)

/**
 * Which fields a section kind holds — the same rule the server and the database check (`ProductDimensionShape.Rules`,
 * `CK_ProductCardDimensions_Shape`). The kg/m is not among them: it comes from the company's table (`SECTION_TABLE_WEIGHT`).
 */
export const SECTION_FIELDS: Record<ProductSectionKind, Shape> = {
  Sheet: SHEET,
  Coil: shape(R, R, F, F, F, F, F, F, F),
  RoundTube: TUBE,
  Profile: BOX,
  EqualAngle: shape(R, R, O, F, F, F, F, F, F),
  UnequalAngle: shape(R, R, O, R, F, F, F, F, F),
  FlatBar: shape(R, R, O, F, F, F, F, F, F),
  NarrowFlatBar: shape(R, R, O, F, F, F, F, F, F),
  RoundBar: shape(F, F, O, F, R, F, F, F, F),
  SquareBar: shape(F, R, O, F, F, F, F, F, F),
  HexBar: shape(F, R, O, F, F, F, F, F, F),
  TeeBar: shape(R, R, O, R, F, F, F, F, F),
  StructuralProfile: shape(F, F, O, F, F, F, F, R, F),
  Rebar: shape(F, F, O, F, R, F, F, F, F),
  WireRod: shape(F, F, F, F, R, F, F, F, F),
  WeldedMesh: shape(F, R, R, F, R, F, R, F, F),
  ChequeredSheet: SHEET,
  PerforatedSheet: SHEET,
  CorrugatedSheet: SHEET,
  ErwTube: TUBE,
  PaintedErwTube: PAINTED_TUBE,
  ErwProfile: BOX,
  PaintedErwProfile: PAINTED_BOX,
}

/** The millimetre measures of each kind (a view of `SECTION_FIELDS`). */
export const SECTION_MEASURES: Record<ProductSectionKind, Readonly<Record<DimensionMeasure, MeasurePresence>>> = SECTION_FIELDS

/**
 * Where a kind's kg/m comes from (URN-D16, KLN-D87): a profile (also ERW, painted or not) cannot be saved without a matching
 * company table row; a round tube's (also ERW) table value is optional; a structural profile reads the standard series rows of
 * the table when there is one (the standard table source is not decided — "kaynak yok" otherwise); every other kind has none
 * (fail-closed: no kg/m is guessed).
 */
export const SECTION_TABLE_WEIGHT: Record<ProductSectionKind, 'required' | 'optional' | 'standard' | 'none'> = {
  Sheet: 'none',
  Coil: 'none',
  RoundTube: 'optional',
  Profile: 'required',
  EqualAngle: 'none',
  UnequalAngle: 'none',
  FlatBar: 'none',
  NarrowFlatBar: 'none',
  RoundBar: 'none',
  SquareBar: 'none',
  HexBar: 'none',
  TeeBar: 'none',
  StructuralProfile: 'standard',
  Rebar: 'none',
  WireRod: 'none',
  WeldedMesh: 'none',
  ChequeredSheet: 'none',
  PerforatedSheet: 'none',
  CorrugatedSheet: 'none',
  ErwTube: 'optional',
  PaintedErwTube: 'optional',
  ErwProfile: 'required',
  PaintedErwProfile: 'required',
}

/** The kinds whose wall must fit twice in the outer diameter (tubes) or in the width and height (box profiles). */
export const WALL_OUTERS: Partial<Record<ProductSectionKind, readonly DimensionMeasure[]>> = {
  RoundTube: ['outerDiameterMm'],
  ErwTube: ['outerDiameterMm'],
  PaintedErwTube: ['outerDiameterMm'],
  Profile: ['widthMm', 'heightMm'],
  ErwProfile: ['widthMm', 'heightMm'],
  PaintedErwProfile: ['widthMm', 'heightMm'],
}

/** The decimals each millimetre measure may carry (`ProductDimensionInputLimitsV1`). */
export const MEASURE_DECIMALS: Record<DimensionMeasure, number> = {
  thicknessMm: 3, widthMm: 3, lengthMm: 3, heightMm: 3, outerDiameterMm: 3, wallThicknessMm: 3, meshSpacingMm: 3,
}

export const DIMENSION_PROBLEM = {
  versionConflict: 'product_dimension_version_conflict',
  reasonRequired: 'product_dimension_reason_required',
  measureInvalid: 'product_dimension_measure_invalid',
  idempotencyConflict: 'product_dimension_idempotency_conflict',
  /** URN-D16: the company's current kg/m table does not hold this profile's width, height and wall. */
  weightSourceMissing: 'product_dimension_weight_source_missing',
  /** URN-D19: both directions (width and height swapped) of this profile stand in the table with different kg/m. */
  weightSourceConflict: 'product_dimension_weight_source_conflict',
} as const

export type DimensionMeasures = Readonly<Record<DimensionMeasure, number | null>>

export type DimensionTexts = Readonly<Record<DimensionTextField, string | null>>

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
  /** KLN-D87: the structural profile series / size and the painted ERW surface (null where the kind takes none). */
  readonly texts: DimensionTexts
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
  'updatedInCompanyId', 'kgPerMetreSourceLabel', 'kgPerMetreTableVersion', ...DIMENSION_TEXTS,
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

/** True when a text field value is well formed (the server's rule; never trimmed or re-cased here). */
export function isTextValue(field: DimensionTextField, value: string): boolean {
  if (field === 'profileSeries') return (PROFILE_SERIES as readonly string[]).includes(value)
  if (field === 'profileSize') return value.length <= PROFILE_SIZE_MAXIMUM_LENGTH && /^[A-Z0-9.]+$/.test(value)
  return value.length > 0 && value.length <= SURFACE_FINISH_MAXIMUM_LENGTH && value.trim() === value
    // eslint-disable-next-line no-control-regex
    && !/[\u0000-\u001f\u007f-\u009f]/.test(value)
}

/** True when the texts follow the kind's shape: required ones well formed, forbidden ones null. */
export function textsFitSection(kind: ProductSectionKind, texts: Readonly<Record<DimensionTextField, unknown>>): boolean {
  return DIMENSION_TEXTS.every((field) => {
    const presence = SECTION_FIELDS[kind][field]
    const value = texts[field]
    if (value === null) return presence !== 'required'
    return presence !== 'forbidden' && typeof value === 'string' && isTextValue(field, value)
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
  const texts = Object.fromEntries(DIMENSION_TEXTS.map((field) => [field, raw[field]])) as Record<DimensionTextField, unknown>
  if (kind === null ? DIMENSION_TEXTS.some((field) => texts[field] !== null) : !textsFitSection(kind as ProductSectionKind, texts)) {
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
    texts: texts as DimensionTexts,
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

export interface SetDimensionBody extends DimensionMeasures, DimensionTexts {
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

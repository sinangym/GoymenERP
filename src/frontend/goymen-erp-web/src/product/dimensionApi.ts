/**
 * BS-9 (2026-11-11) — the "Ebat/kalınlık" row of a product, ONE row on the shared product card (the same in every company; a
 * holder in any company changes it), pinned to `GET/PUT /api/v1/product-dimensions/{productId}` (`ProductDimensionEndpoints`).
 * The U-08 unit conversion reads its input from this row; a product without one has "kaynak yok". Reads send the selected
 * company only; the command carries one caller-owned `Idempotency-Key` (kept for a retry after an unknown outcome). Success is
 * exactly the contract's status; a 5xx or an off-contract answer is an UNKNOWN outcome; a refusal is read by its status and
 * `code`. The measures are never rounded here: the server refuses a value with more decimals than its column holds (URN-D8).
 * KLN-D48 (STK-S37) adds the long products, structural profiles, rebar/wire rod/mesh, sheet derivatives and (painted) ERW; a
 * structural profile's kg/m is read from the company's standard weight table (KLN-D54) and shown with its source, never typed.
 */

export const PRODUCT_SECTION_KINDS = [
  'Sheet',
  'Coil',
  'RoundTube',
  'Profile',
  'EqualAngle',
  'UnequalAngle',
  'FlatBar',
  'NarrowFlatBar',
  'RoundBar',
  'SquareBar',
  'HexBar',
  'TeeBar',
  'StructuralProfile',
  'Rebar',
  'WireRod',
  'WeldedMesh',
  'ChequeredSheet',
  'PerforatedSheet',
  'CorrugatedSheet',
  'ErwTube',
  'ErwProfile',
  'PaintedErwTube',
  'PaintedErwProfile',
] as const
export type ProductSectionKind = (typeof PRODUCT_SECTION_KINDS)[number]

/** KLN-D48 (2): the structural profile series (`ProductProfileSeriesV1`). */
export const PROFILE_SERIES = ['UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM'] as const
export type ProfileSeries = (typeof PROFILE_SERIES)[number]

export const DIMENSION_MEASURES = [
  'thicknessMm',
  'widthMm',
  'lengthMm',
  'heightMm',
  'outerDiameterMm',
  'wallThicknessMm',
  'meshSpacingMm',
  'kgPerMetre',
] as const
export type DimensionMeasure = (typeof DIMENSION_MEASURES)[number]

/** The text fields of a row: the structural profile designation and the painted ERW surface. */
export const DIMENSION_TEXTS = ['profileSeries', 'profileSize', 'surfaceFinish'] as const
export type DimensionTextField = (typeof DIMENSION_TEXTS)[number]

export type DimensionField = DimensionMeasure | DimensionTextField

export type MeasurePresence = 'required' | 'optional' | 'forbidden'

type Shape = Record<DimensionField, MeasurePresence>

const R = 'required'
const O = 'optional'
const F = 'forbidden'

function shape(
  thicknessMm: MeasurePresence, widthMm: MeasurePresence, lengthMm: MeasurePresence, heightMm: MeasurePresence,
  outerDiameterMm: MeasurePresence, wallThicknessMm: MeasurePresence, meshSpacingMm: MeasurePresence,
  kgPerMetre: MeasurePresence, profile: MeasurePresence, surfaceFinish: MeasurePresence,
): Shape {
  return {
    thicknessMm, widthMm, lengthMm, heightMm, outerDiameterMm, wallThicknessMm, meshSpacingMm, kgPerMetre,
    profileSeries: profile, profileSize: profile, surfaceFinish,
  }
}

const SHEET = shape(R, R, R, F, F, F, F, F, F, F)
const TUBE = shape(F, F, F, F, R, R, F, O, F, F)
const BOX = shape(F, R, F, R, F, R, F, R, F, F)

/**
 * Which fields a section kind holds — the same rule table the server and the database check (`ProductDimensionShape`,
 * `CK_ProductCardDimensions_Shape`). A round/ERW tube's kg/m is the optional table value; a box/ERW profile's kg/m is required;
 * a structural profile carries its series and size designation and NEVER a typed kg/m (KLN-D54: it comes from the standard table).
 */
export const SECTION_MEASURES: Record<ProductSectionKind, Shape> = {
  Sheet: SHEET,
  Coil: shape(R, R, F, F, F, F, F, F, F, F),
  RoundTube: TUBE,
  Profile: BOX,
  EqualAngle: shape(R, R, O, F, F, F, F, F, F, F),
  UnequalAngle: shape(R, R, O, R, F, F, F, F, F, F),
  FlatBar: shape(R, R, O, F, F, F, F, F, F, F),
  NarrowFlatBar: shape(R, R, O, F, F, F, F, F, F, F),
  RoundBar: shape(F, F, O, F, R, F, F, F, F, F),
  SquareBar: shape(F, R, O, F, F, F, F, F, F, F),
  HexBar: shape(F, R, O, F, F, F, F, F, F, F),
  TeeBar: shape(R, R, O, R, F, F, F, F, F, F),
  StructuralProfile: shape(F, F, R, F, F, F, F, F, R, F),
  Rebar: shape(F, F, O, F, R, F, F, F, F, F),
  WireRod: shape(F, F, F, F, R, F, F, F, F, F),
  WeldedMesh: shape(F, R, R, F, R, F, R, F, F, F),
  ChequeredSheet: SHEET,
  PerforatedSheet: SHEET,
  CorrugatedSheet: SHEET,
  ErwTube: TUBE,
  ErwProfile: BOX,
  PaintedErwTube: { ...TUBE, surfaceFinish: R },
  PaintedErwProfile: { ...BOX, surfaceFinish: R },
}

/** Where the usable kg/m of a row comes from (`ProductKgPerMetreSourcesV1`). */
export const KG_PER_METRE_SOURCES = ['Card', 'StandardTable', 'NoSource'] as const
export type KgPerMetreSource = (typeof KG_PER_METRE_SOURCES)[number]

/** The decimals each measure may carry (`ProductDimensionInputLimitsV1`): millimetres three, kg/m six. */
export const MEASURE_DECIMALS: Record<DimensionMeasure, number> = {
  thicknessMm: 3, widthMm: 3, lengthMm: 3, heightMm: 3, outerDiameterMm: 3, wallThicknessMm: 3, meshSpacingMm: 3, kgPerMetre: 6,
}

/** `ProductDimensionInputLimitsV1`: the designation and surface text lengths. */
export const PROFILE_SIZE_MAXIMUM_LENGTH = 16
export const SURFACE_FINISH_MAXIMUM_LENGTH = 200

/** A structural profile size designation as the server accepts it: 1..16 of capitals, digits and dots. */
export function isProfileSize(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= PROFILE_SIZE_MAXIMUM_LENGTH && /^[A-Z0-9.]+$/.test(value)
}

/** A surface (paint/colour) text as the server accepts it: no leading/trailing space, no control character. */
export function isSurfaceFinish(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= SURFACE_FINISH_MAXIMUM_LENGTH
    && value.trim() === value && !/[\u0000-\u001f\u007f-\u009f]/.test(value)
}

export const DIMENSION_PROBLEM = {
  versionConflict: 'product_dimension_version_conflict',
  reasonRequired: 'product_dimension_reason_required',
  measureInvalid: 'product_dimension_measure_invalid',
  idempotencyConflict: 'product_dimension_idempotency_conflict',
} as const

export type DimensionMeasures = Readonly<Record<DimensionMeasure, number | null>>

export type DimensionTexts = Readonly<Record<DimensionTextField, string | null>>

/** The usable kg/m of a structural profile and where it came from (KLN-D54). */
export interface DimensionWeight {
  /** Null while the table or its row is missing ("kaynak yok"). */
  readonly standardKgPerMetre: number | null
  readonly source: KgPerMetreSource | null
  readonly tableVersion: string | null
  readonly tableSourceLabel: string | null
}

export interface ProductDimensionView {
  readonly productId: string
  readonly version: string
  readonly sectionKind: ProductSectionKind | null
  readonly measures: DimensionMeasures
  readonly texts: DimensionTexts
  readonly weight: DimensionWeight
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
  'companyId', 'productId', 'version', 'sectionKind', ...DIMENSION_MEASURES, ...DIMENSION_TEXTS, 'updatedAtUtc', 'updatedBy',
  'updatedInCompanyId', 'standardKgPerMetre', 'kgPerMetreSource', 'weightTableVersion', 'weightTableSourceLabel',
] as const

function isPositiveNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function isTextValue(field: DimensionTextField, value: unknown): boolean {
  if (field === 'profileSeries') return PROFILE_SERIES.includes(value as ProfileSeries)
  if (field === 'profileSize') return isProfileSize(value)
  return isSurfaceFinish(value)
}

/** True when the fields follow the kind's shape: required ones present and well-formed, forbidden ones null. */
export function fitsSection(kind: ProductSectionKind, fields: Readonly<Record<DimensionField, unknown>>): boolean {
  const measuresFit = DIMENSION_MEASURES.every((measure) => {
    const presence = SECTION_MEASURES[kind][measure]
    const value = fields[measure]
    if (value === null) return presence !== 'required'
    return presence !== 'forbidden' && isPositiveNumber(value)
  })
  return measuresFit && DIMENSION_TEXTS.every((field) => {
    const presence = SECTION_MEASURES[kind][field]
    const value = fields[field]
    if (value === null) return presence !== 'required'
    return presence !== 'forbidden' && isTextValue(field, value)
  })
}

const VERSION = /^(0|[1-9]\d*)$/

/** The weight members: a structural profile names its table source; other kinds say "Card" only when the card holds kg/m. */
function decodeWeight(raw: Record<string, unknown>, kind: ProductSectionKind | null): DimensionWeight | null {
  const standard = raw.standardKgPerMetre
  const source = raw.kgPerMetreSource
  const tableVersion = raw.weightTableVersion
  const label = raw.weightTableSourceLabel
  if (!(standard === null || isPositiveNumber(standard))) return null
  if (!(source === null || KG_PER_METRE_SOURCES.includes(source as KgPerMetreSource))) return null
  if (!(tableVersion === null || (typeof tableVersion === 'string' && VERSION.test(tableVersion) && tableVersion !== '0'))) return null
  if (!(label === null || typeof label === 'string')) return null
  if (kind === 'StructuralProfile') {
    if (source === 'StandardTable' ? standard === null || tableVersion === null : source !== 'NoSource' || standard !== null) {
      return null
    }
    if ((tableVersion === null) !== (label === null)) return null
  } else {
    const expected = kind !== null && raw.kgPerMetre !== null ? 'Card' : null
    if (source !== expected || standard !== null || tableVersion !== null || label !== null) return null
  }
  return {
    standardKgPerMetre: standard as number | null,
    source: source as KgPerMetreSource | null,
    tableVersion: tableVersion as string | null,
    tableSourceLabel: label as string | null,
  }
}

export function decodeDimension(raw: unknown, companyId: string, productId: string): ProductDimensionView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, DIMENSION_VIEW_KEYS) || raw.companyId !== companyId || raw.productId !== productId
    || typeof raw.version !== 'string' || !VERSION.test(raw.version)) {
    return null
  }
  if (!(raw.updatedAtUtc === null || typeof raw.updatedAtUtc === 'string')
    || !(raw.updatedBy === null || typeof raw.updatedBy === 'string')) return null
  const kind = raw.sectionKind
  if (kind !== null && !PRODUCT_SECTION_KINDS.includes(kind as ProductSectionKind)) return null
  if ((raw.version === '0') !== (kind === null)) return null
  const fields = Object.fromEntries(
    [...DIMENSION_MEASURES, ...DIMENSION_TEXTS].map((field) => [field, raw[field]]),
  ) as Record<DimensionField, unknown>
  if (kind === null
    ? [...DIMENSION_MEASURES, ...DIMENSION_TEXTS].some((field) => fields[field] !== null)
    : !fitsSection(kind as ProductSectionKind, fields)) {
    return null
  }
  const weight = decodeWeight(raw, kind as ProductSectionKind | null)
  if (weight === null) return null
  const updatedIn = raw.updatedInCompanyId
  if (kind === null ? updatedIn !== null : typeof updatedIn !== 'string' || !UUID.test(updatedIn)) return null
  return {
    productId,
    version: raw.version,
    sectionKind: kind as ProductSectionKind | null,
    measures: Object.fromEntries(DIMENSION_MEASURES.map((measure) => [measure, fields[measure]])) as DimensionMeasures,
    texts: Object.fromEntries(DIMENSION_TEXTS.map((field) => [field, fields[field]])) as DimensionTexts,
    weight,
    changedInAnotherCompany: typeof updatedIn === 'string' && updatedIn.toLowerCase() !== companyId.toLowerCase(),
  }
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

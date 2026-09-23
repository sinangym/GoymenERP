export interface ProductStockCandidate {
  readonly productId: string
  readonly productVersion: string
  readonly code: string
  readonly name: string
  readonly stockUnitId: string
  readonly stockUnitCode: string
  readonly profileId: string
  readonly profileVersion: string
}

export interface ProductStockCandidates {
  readonly items: readonly ProductStockCandidate[]
  readonly searchWindowFull: boolean
}

export interface OrganizationLocationCandidate {
  readonly locationId: string
  readonly locationCode: string
  readonly locationName: string
  readonly siteId: string
  readonly siteCode: string
  readonly siteName: string
}

export interface OrganizationLocationCandidates {
  readonly items: readonly OrganizationLocationCandidate[]
  readonly searchWindowFull: boolean
}

export interface OpeningCountLineRequest {
  readonly lineId: string
  readonly labelId: string
  readonly labelCode: string
  readonly lotId: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly productId: string
  readonly productVersion: string
  readonly quantityKg: string
}

export interface OpeningCountRequest {
  readonly countId: string
  readonly locationId: string
  readonly countedOn: string
  readonly sheetReference: string
  readonly lines: readonly OpeningCountLineRequest[]
}

export interface OpeningCountLineView {
  readonly lineId: string
  readonly labelId: string
  readonly labelCode: string
  readonly lotId: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly productId: string
  readonly quantityKg: string
}

export interface OpeningCountView {
  readonly countId: string
  readonly companyId: string
  readonly locationId: string
  readonly countedOn: string
  readonly sheetReference: string
  readonly lines: readonly OpeningCountLineView[]
}

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const VERSION = /^[1-9]\d*$/
const DECIMAL = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
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

function canonicalVersion(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 19 && VERSION.test(value) && BigInt(value) <= MAX_INT64
}

function dateOnly(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const [year, month, day] = value.split('-').map(Number)
  if (year < 1 || month < 1 || month > 12 || day < 1) return false
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  return day <= days[month - 1]
}

function quantity(value: unknown): value is string {
  return typeof value === 'string' && DECIMAL.test(value) && /[1-9]/.test(value)
}

function nullableText(value: unknown): value is string | null {
  return value === null || (typeof value === 'string' && value.trim().length > 0)
}

function productCandidate(value: unknown): ProductStockCandidate | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'productId', 'productVersion', 'code', 'name', 'stockUnitId', 'stockUnitCode', 'profileId', 'profileVersion',
  ]) || !guid(source.productId) || !canonicalVersion(source.productVersion) || !text(source.code) ||
    !text(source.name) || !guid(source.stockUnitId) || !text(source.stockUnitCode) ||
    !guid(source.profileId) || !canonicalVersion(source.profileVersion)) return null
  return source as unknown as ProductStockCandidate
}

function locationCandidate(value: unknown): OrganizationLocationCandidate | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'locationId', 'locationCode', 'locationName', 'siteId', 'siteCode', 'siteName',
  ]) || !guid(source.locationId) || !text(source.locationCode) || !text(source.locationName) ||
    !guid(source.siteId) || !text(source.siteCode) || !text(source.siteName)) return null
  return source as unknown as OrganizationLocationCandidate
}

function openingLine(value: unknown): OpeningCountLineView | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'lineId', 'labelId', 'labelCode', 'lotId', 'heatNumber', 'coilNumber', 'productId', 'quantityKg',
  ]) || !guid(source.lineId) || !guid(source.labelId) || !text(source.labelCode) || !guid(source.lotId) ||
    !nullableText(source.heatNumber) || !nullableText(source.coilNumber) || !guid(source.productId) ||
    !quantity(source.quantityKg)) return null
  return source as unknown as OpeningCountLineView
}

function openingRequestLine(value: unknown): OpeningCountLineRequest | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'lineId', 'labelId', 'labelCode', 'lotId', 'heatNumber', 'coilNumber', 'productId', 'productVersion', 'quantityKg',
  ]) || !guid(source.lineId) || !guid(source.labelId) || !text(source.labelCode) || !guid(source.lotId) ||
    !nullableText(source.heatNumber) || !nullableText(source.coilNumber) || !guid(source.productId) ||
    !canonicalVersion(source.productVersion) || !quantity(source.quantityKg)) return null
  return source as unknown as OpeningCountLineRequest
}

export function decodeProductStockCandidates(value: unknown): ProductStockCandidates | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'searchWindowFull']) || !Array.isArray(source.items) ||
    typeof source.searchWindowFull !== 'boolean') return null
  const items = source.items.map(productCandidate)
  if (items.some((item) => item === null)) return null
  const decoded = items as ProductStockCandidate[]
  if (new Set(decoded.map((item) => item.productId)).size !== decoded.length) return null
  return { items: decoded, searchWindowFull: source.searchWindowFull }
}

export function decodeOrganizationLocationCandidates(value: unknown): OrganizationLocationCandidates | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'searchWindowFull']) || !Array.isArray(source.items) ||
    typeof source.searchWindowFull !== 'boolean') return null
  const items = source.items.map(locationCandidate)
  if (items.some((item) => item === null)) return null
  const decoded = items as OrganizationLocationCandidate[]
  if (new Set(decoded.map((item) => item.locationId)).size !== decoded.length) return null
  return { items: decoded, searchWindowFull: source.searchWindowFull }
}

export function decodeOpeningCount(value: unknown): OpeningCountView | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'countId', 'companyId', 'locationId', 'countedOn', 'sheetReference', 'lines',
  ]) || !guid(source.countId) || !guid(source.companyId) || !guid(source.locationId) ||
    !dateOnly(source.countedOn) || !text(source.sheetReference) || !Array.isArray(source.lines) ||
    source.lines.length < 1) return null
  const lines = source.lines.map(openingLine)
  if (lines.some((line) => line === null)) return null
  const decoded = lines as OpeningCountLineView[]
  if (new Set(decoded.map((line) => line.lineId)).size !== decoded.length ||
    new Set(decoded.map((line) => line.labelId)).size !== decoded.length ||
    new Set(decoded.map((line) => line.labelCode)).size !== decoded.length) return null
  return { ...source, lines: decoded } as unknown as OpeningCountView
}

export function isOpeningCountRequest(value: unknown): value is OpeningCountRequest {
  const source = record(value)
  if (source === null || !exact(source, ['countId', 'locationId', 'countedOn', 'sheetReference', 'lines']) ||
    !guid(source.countId) || !guid(source.locationId) || !dateOnly(source.countedOn) ||
    !text(source.sheetReference) || !Array.isArray(source.lines) || source.lines.length < 1) return false
  const lines = source.lines.map(openingRequestLine)
  if (lines.some((line) => line === null)) return false
  const decoded = lines as OpeningCountLineRequest[]
  return new Set(decoded.map((line) => line.lineId)).size === decoded.length &&
    new Set(decoded.map((line) => line.labelId)).size === decoded.length &&
    new Set(decoded.map((line) => line.labelCode)).size === decoded.length
}

export function isDateOnly(value: string): boolean {
  return dateOnly(value)
}

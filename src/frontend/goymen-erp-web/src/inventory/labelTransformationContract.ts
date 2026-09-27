/**
 * R19 / AT-038 — one same-company label split or merge (DEC-LBL-002/003).
 *
 * Owner contract: `POST /api/v1/inventory/label-transformations`
 * (`InventoryLabelTransformationApiV1`, `TransformInventoryLabelsV1` → `InventoryLabelTransformationViewV1`).
 * The server decides everything that matters: the sources' stored position and exact kg, the lot/trace
 * facts copied onto the outputs, active-allocation and version conflicts. This module only builds a request
 * the writer can accept and refuses to show a response that does not describe that request.
 */

export type LabelTransformationKind = 'Split' | 'Merge'

export interface LabelTransformationSource {
  readonly labelId: string
  readonly expectedLabelVersion: string
}

export interface LabelTransformationOutput {
  readonly labelId: string
  readonly code: string
  readonly quantityKg: string
}

export interface LabelTransformationRequest {
  readonly transformationId: string
  readonly kind: LabelTransformationKind
  readonly sources: readonly LabelTransformationSource[]
  readonly outputs: readonly LabelTransformationOutput[]
  readonly occurredAtUtc: string
  readonly reason: string
}

export interface LabelTransformationView {
  readonly transformationId: string
  readonly companyId: string
  readonly kind: LabelTransformationKind
  readonly occurredAtUtc: string
  readonly reason: string
  readonly sourceLabelIds: readonly string[]
  readonly outputLabelIds: readonly string[]
}

/** The same bounds the writer enforces (`MaterialLabel.MaximumCodeLength`, reason ≤ 1000). */
export const LABEL_CODE_MAX_LENGTH = 64
export const REASON_MAX_LENGTH = 1000
/** The kilogram contract (Ö2): at most three decimals; more is refused, never rounded. */
export const KG_MAX_DECIMALS = 3

type JsonObject = Record<string, unknown>

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const VERSION = /^[1-9]\d*$/
const DECIMAL = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$/
const MAX_INT64 = 9223372036854775807n

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

export function isGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID
}

function isVersion(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 19 && VERSION.test(value) && BigInt(value) <= MAX_INT64
}

function isKind(value: unknown): value is LabelTransformationKind {
  return value === 'Split' || value === 'Merge'
}

function isUtcInstant(value: unknown): value is string {
  return typeof value === 'string' && UTC_INSTANT.test(value) && !Number.isNaN(Date.parse(value))
}

function hasControl(value: string): boolean {
  for (const character of value) {
    const code = character.codePointAt(0) ?? 0
    if (code < 0x20 || (code >= 0x7f && code < 0xa0)) return true
  }
  return false
}

/** A positive, canonical kg text with at most three decimals. */
export function isKilogramText(value: unknown): value is string {
  return typeof value === 'string' && DECIMAL.test(value) && /[1-9]/.test(value) &&
    (value.split('.')[1]?.length ?? 0) <= KG_MAX_DECIMALS
}

/** Canonical kg text → integer grams, so sums never pass through a JS `number`. */
export function kilogramsToGrams(value: string): bigint {
  const [whole, fraction = ''] = value.split('.')
  return BigInt(whole) * 1000n + BigInt(fraction.padEnd(KG_MAX_DECIMALS, '0'))
}

/** Integer grams → canonical kg text (no trailing fraction zeros), as the server writes it. */
export function gramsToKilograms(grams: bigint): string {
  const negative = grams < 0n
  const magnitude = negative ? -grams : grams
  const whole = magnitude / 1000n
  const fraction = (magnitude % 1000n).toString().padStart(KG_MAX_DECIMALS, '0').replace(/0+$/, '')
  return `${negative ? '-' : ''}${whole.toString()}${fraction.length > 0 ? `.${fraction}` : ''}`
}

export function sumKilograms(values: readonly string[]): string {
  return gramsToKilograms(values.reduce((total, value) => total + kilogramsToGrams(value), 0n))
}

/**
 * The writer compares label codes case-insensitively (`StringComparer.OrdinalIgnoreCase`), so "K-1" and "k-1"
 * are the same code there; the client uses the same rule so a duplicate is caught before sending.
 */
export function labelCodeKey(code: string): string {
  return code.toUpperCase()
}

export function hasDuplicateLabelCodes(codes: readonly string[]): boolean {
  return new Set(codes.map(labelCodeKey)).size !== codes.length
}

/** Label ids are GUIDs; the server may answer them in either case. */
export function sameLabelId(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase()
}

export function isLabelCode(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value === value.trim() &&
    value.length <= LABEL_CODE_MAX_LENGTH && !hasControl(value)
}

export function isReason(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value === value.trim() &&
    value.length <= REASON_MAX_LENGTH && !hasControl(value.replace(/[\r\n\t]/g, ' '))
}

function source(value: unknown): value is LabelTransformationSource {
  const item = record(value)
  return item !== null && exact(item, ['labelId', 'expectedLabelVersion']) &&
    isGuid(item.labelId) && isVersion(item.expectedLabelVersion)
}

function output(value: unknown): value is LabelTransformationOutput {
  const item = record(value)
  return item !== null && exact(item, ['labelId', 'code', 'quantityKg']) &&
    isGuid(item.labelId) && isLabelCode(item.code) && isKilogramText(item.quantityKg)
}

/**
 * The request shape the writer accepts, including the shape rules its domain checks (one whole source into
 * at least two outputs for a split, at least two whole sources into one output for a merge, distinct ids and
 * codes). Used to re-validate a remembered attempt before resending it.
 */
export function isLabelTransformationRequest(value: unknown): value is LabelTransformationRequest {
  const request = record(value)
  if (request === null || !exact(request, ['transformationId', 'kind', 'sources', 'outputs', 'occurredAtUtc', 'reason'])) return false
  if (!isGuid(request.transformationId) || !isKind(request.kind) || !isUtcInstant(request.occurredAtUtc) ||
    !isReason(request.reason) || !Array.isArray(request.sources) || !Array.isArray(request.outputs)) return false
  const sources = request.sources as unknown[]
  const outputs = request.outputs as unknown[]
  if (!sources.every(source) || !outputs.every(output)) return false
  const typedSources = sources as LabelTransformationSource[]
  const typedOutputs = outputs as LabelTransformationOutput[]
  if (request.kind === 'Split' && (typedSources.length !== 1 || typedOutputs.length < 2)) return false
  if (request.kind === 'Merge' && (typedSources.length < 2 || typedOutputs.length !== 1)) return false
  const ids = [request.transformationId, ...typedSources.map((item) => item.labelId), ...typedOutputs.map((item) => item.labelId)]
    .map((id) => (id as string).toLowerCase())
  const codes = typedOutputs.map((item) => item.code)
  return new Set(ids).size === ids.length && !hasDuplicateLabelCodes(codes)
}

function guidList(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(isGuid)
}

export function decodeLabelTransformationView(value: unknown): LabelTransformationView | null {
  const view = record(value)
  if (view === null || !exact(view, ['transformationId', 'companyId', 'kind', 'occurredAtUtc', 'reason', 'sourceLabelIds', 'outputLabelIds'])) return null
  if (!isGuid(view.transformationId) || !isGuid(view.companyId) || !isKind(view.kind) ||
    !isUtcInstant(view.occurredAtUtc) || typeof view.reason !== 'string' || view.reason.trim().length === 0 ||
    !guidList(view.sourceLabelIds) || !guidList(view.outputLabelIds)) return null
  return {
    transformationId: view.transformationId,
    companyId: view.companyId,
    kind: view.kind,
    occurredAtUtc: view.occurredAtUtc,
    reason: view.reason,
    sourceLabelIds: view.sourceLabelIds,
    outputLabelIds: view.outputLabelIds,
  }
}

function sameIds(left: readonly string[], right: readonly string[]): boolean {
  const normalizedLeft = left.map((id) => id.toLowerCase()).sort()
  const normalizedRight = right.map((id) => id.toLowerCase()).sort()
  return normalizedLeft.length === normalizedRight.length &&
    normalizedLeft.every((id, index) => id === normalizedRight[index])
}

/** A response is shown only when it is the answer to THIS request in THIS company. */
export function describesTransformation(
  view: LabelTransformationView,
  request: LabelTransformationRequest,
  companyId: string,
): boolean {
  return view.companyId.toLowerCase() === companyId.toLowerCase() &&
    view.transformationId.toLowerCase() === request.transformationId.toLowerCase() &&
    view.kind === request.kind && view.reason === request.reason &&
    Date.parse(view.occurredAtUtc) === Date.parse(request.occurredAtUtc) &&
    sameIds(view.sourceLabelIds, request.sources.map((item) => item.labelId)) &&
    sameIds(view.outputLabelIds, request.outputs.map((item) => item.labelId))
}

/**
 * The coded 503s the command raises BEFORE any write (`InventoryLabelTransformationHttpCodesV1.Status503Codes`):
 * the permission name is not registered, the stored label state cannot be read, or the company's Settings
 * kilogram profile cannot be used. Nothing was recorded, so such an answer is a definite failure, not unknown.
 * A code-less 503, or any other 5xx, stays unknown: it may have happened after the save.
 */
export const DEFINITE_UNAVAILABLE_CODES: ReadonlySet<string> = new Set([
  'inventory_permission_unregistered',
  'inventory_label_genealogy_unavailable',
  'inventory_kg_profile_unusable',
  'settings_provider_unavailable',
  'settings_unit_profile_not_found',
  'settings_profile_invalid',
  'settings_access_denied',
  'settings_decimal_invalid',
  'settings_decimal_scale_exceeded',
])

const REFUSAL_TEXT: Readonly<Record<string, string>> = {
  validation_failed: 'İstek geçersiz: kaynak, çıktı kodu, kg veya gerekçe kurallara uymuyor.',
  idempotency_conflict: 'Bu gönderim anahtarı başka bir içerikle kullanılmış. Formu yeniden açıp tekrar deneyin.',
  inventory_label_not_found: 'Kaynak etiketlerden biri bu şirkette bulunamadı.',
  inventory_label_version_conflict: 'Kaynak etiket siz formu açtıktan sonra değişti. Etiketi yeniden açıp tekrar deneyin.',
  inventory_label_source_position_conflict: 'Kaynak etiketin tek bir fiziksel pozisyonu olmalı (serbest ve bloklu miktar aynı anda olamaz).',
  inventory_label_active_allocation_conflict: 'Kaynak etiket üzerinde aktif tahsis var; önce tahsisin serbest bırakılması gerekir.',
  inventory_label_code_taken: 'Çıktı etiket kodlarından biri bu şirkette zaten kullanılıyor.',
  inventory_label_transformation_conflict: 'Bu işlem kimliği farklı bir içerikle kayıtlı.',
  inventory_concurrent_change: 'Aynı etiket eşzamanlı değişti. Etiketi yeniden açıp tekrar deneyin.',
  inventory_kg_scale_exceeded: 'Bir kg değeri şirketin onaylı kg ondalık hassasiyetini aşıyor.',
  forbidden: 'Bu şirkette etiket bölme/birleştirme yetkiniz yok.',
  inventory_permission_unregistered: 'Etiket bölme/birleştirme bu sunucuda henüz yayımlanmadı; hiçbir şey kaydedilmedi.',
  inventory_label_genealogy_unavailable: 'Etiketin kayıtlı durumu okunamadı; hiçbir şey kaydedilmedi. Etiketi yeniden açın.',
  inventory_kg_profile_unusable: 'Şirketin onaylı kg profili bu tarih için kullanılamıyor; hiçbir şey kaydedilmedi.',
  unauthenticated: 'Oturum doğrulanamadı.',
}

export function transformationRefusalText(status: number, code: string | null): string {
  if (code !== null && REFUSAL_TEXT[code] !== undefined) return REFUSAL_TEXT[code]
  if (status === 403) return REFUSAL_TEXT.forbidden
  if (status === 401) return REFUSAL_TEXT.unauthenticated
  if (status === 503) return `Etiket dönüşümü şu an kullanılamıyor (${code ?? status}); hiçbir şey kaydedilmedi.`
  return `Etiket dönüşümü kaydedilmedi (${code ?? status}).`
}

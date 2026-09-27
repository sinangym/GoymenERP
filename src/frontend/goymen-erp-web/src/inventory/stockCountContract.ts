/**
 * ST-05 / AT-037 — stock count with difference reason and authority (DEC-STK-007).
 *
 * Owner contract (`InventoryStockCountApiV1`):
 * - `POST /api/v1/inventory/stock-counts` (`RecordInventoryStockCountV1` → `InventoryStockCountViewV1`)
 * - `POST /api/v1/inventory/stock-counts/{countId}/decision` (`DecideInventoryStockCountV1` → view)
 * - `GET /api/v1/inventory/labels/{labelId}/stock-counts` (`InventoryLabelStockCountsV1`)
 *
 * A count changes no stock; its difference (counted − book) is final only after another actor approves it. The
 * server decides the book, the position and every conflict; this module only builds requests the writer can
 * accept and refuses to show a response that does not describe that request.
 */
import { gramsToKilograms, isGuid, isReason, KG_MAX_DECIMALS, kilogramsToGrams, REASON_MAX_LENGTH } from './labelTransformationContract'

export { REASON_MAX_LENGTH }

export type StockCountStatus = 'NoDifference' | 'PendingApproval' | 'Approved' | 'Rejected'
export type StockCountDecisionKind = 'Approve' | 'Reject'

export interface StockCountRequest {
  readonly countId: string
  readonly labelId: string
  readonly expectedLabelVersion: string
  readonly countedKg: string
  readonly countedAtUtc: string
  readonly reason: string
}

export interface StockCountDecisionRequest {
  readonly decision: StockCountDecisionKind
  readonly reason: string
}

export interface StockCountDecisionView {
  readonly decision: 'Approved' | 'Rejected'
  readonly decidedAtUtc: string
  readonly reason: string
  readonly adjustmentMovementId: string | null
}

export interface StockCountView {
  readonly countId: string
  readonly companyId: string
  readonly labelId: string
  readonly labelVersion: string
  readonly ownerKind: string
  readonly ownerId: string
  readonly custodianKind: string
  readonly custodianId: string
  readonly locationId: string
  readonly productId: string
  readonly block: string
  readonly bookKg: string
  readonly countedKg: string
  readonly differenceKg: string
  readonly countedAtUtc: string
  readonly reason: string
  readonly recordedAtUtc: string
  readonly status: StockCountStatus
  readonly decision: StockCountDecisionView | null
}

export interface LabelStockCounts {
  readonly labelId: string
  readonly items: readonly StockCountView[]
  readonly windowFull: boolean
}

type JsonObject = Record<string, unknown>

const NON_NEGATIVE = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
const SIGNED = /^-?(0|[1-9]\d*)(\.\d*[1-9])?$/
const VERSION = /^[1-9]\d*$/
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$/
const STATUSES: readonly StockCountStatus[] = ['NoDifference', 'PendingApproval', 'Approved', 'Rejected']

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function isUtcInstant(value: unknown): value is string {
  return typeof value === 'string' && UTC_INSTANT.test(value) && !Number.isNaN(Date.parse(value))
}

function isVersion(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 19 && VERSION.test(value) && BigInt(value) <= 9223372036854775807n
}

function decimals(value: string): number {
  return value.split('.')[1]?.length ?? 0
}

/** A counted kg: canonical, non-negative (0 = the piece is gone), at most three decimals. */
export function isCountedKilogramText(value: unknown): value is string {
  return typeof value === 'string' && NON_NEGATIVE.test(value) && decimals(value) <= KG_MAX_DECIMALS
}

function isPositiveKilogramText(value: unknown): value is string {
  return isCountedKilogramText(value) && /[1-9]/.test(value)
}

function isSignedKilogramText(value: unknown): value is string {
  return typeof value === 'string' && SIGNED.test(value) && value !== '-0' && decimals(value) <= KG_MAX_DECIMALS
}

function signedToGrams(value: string): bigint {
  return value.startsWith('-') ? -kilogramsToGrams(value.slice(1)) : kilogramsToGrams(value)
}

/** counted − book as the server writes it: canonical, a leading minus for a shortage. */
export function differenceKilograms(bookKg: string, countedKg: string): string {
  return gramsToKilograms(kilogramsToGrams(countedKg) - kilogramsToGrams(bookKg))
}

function decodeDecision(value: unknown): StockCountDecisionView | null {
  const decision = record(value)
  if (decision === null || !exact(decision, ['decision', 'decidedAtUtc', 'reason', 'adjustmentMovementId'])) return null
  if ((decision.decision !== 'Approved' && decision.decision !== 'Rejected') || !isUtcInstant(decision.decidedAtUtc) ||
    typeof decision.reason !== 'string' || decision.reason.trim().length === 0) return null
  const movement = decision.adjustmentMovementId
  if (decision.decision === 'Approved' ? !isGuid(movement) : movement !== null) return null
  return {
    decision: decision.decision,
    decidedAtUtc: decision.decidedAtUtc,
    reason: decision.reason,
    adjustmentMovementId: movement as string | null,
  }
}

const VIEW_FIELDS = [
  'countId', 'companyId', 'labelId', 'labelVersion', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId',
  'locationId', 'productId', 'block', 'bookKg', 'countedKg', 'differenceKg', 'countedAtUtc', 'reason',
  'recordedAtUtc', 'status', 'decision',
] as const

/** One count, only when it is internally consistent: difference = counted − book and the status matches it. */
export function decodeStockCountView(value: unknown): StockCountView | null {
  const view = record(value)
  if (view === null || !exact(view, VIEW_FIELDS)) return null
  if (!isGuid(view.countId) || !isGuid(view.companyId) || !isGuid(view.labelId) || !isVersion(view.labelVersion) ||
    typeof view.ownerKind !== 'string' || !isGuid(view.ownerId) || typeof view.custodianKind !== 'string' ||
    !isGuid(view.custodianId) || !isGuid(view.locationId) || !isGuid(view.productId) ||
    (view.block !== 'Free' && view.block !== 'Blocked') || !isPositiveKilogramText(view.bookKg) ||
    !isCountedKilogramText(view.countedKg) || !isSignedKilogramText(view.differenceKg) ||
    !isUtcInstant(view.countedAtUtc) || !isUtcInstant(view.recordedAtUtc) ||
    typeof view.reason !== 'string' || view.reason.trim().length === 0 ||
    !STATUSES.includes(view.status as StockCountStatus)) return null
  if (signedToGrams(view.differenceKg) !== kilogramsToGrams(view.countedKg) - kilogramsToGrams(view.bookKg)) return null
  const decision = view.decision === null ? null : decodeDecision(view.decision)
  if (view.decision !== null && decision === null) return null
  const hasDifference = view.differenceKg !== '0'
  const consistent = view.status === 'NoDifference' ? !hasDifference && decision === null
    : view.status === 'PendingApproval' ? hasDifference && decision === null
      : hasDifference && decision !== null && decision.decision === view.status
  if (!consistent) return null
  return {
    countId: view.countId,
    companyId: view.companyId,
    labelId: view.labelId,
    labelVersion: view.labelVersion,
    ownerKind: view.ownerKind,
    ownerId: view.ownerId,
    custodianKind: view.custodianKind,
    custodianId: view.custodianId,
    locationId: view.locationId,
    productId: view.productId,
    block: view.block,
    bookKg: view.bookKg,
    countedKg: view.countedKg,
    differenceKg: view.differenceKg,
    countedAtUtc: view.countedAtUtc,
    reason: view.reason,
    recordedAtUtc: view.recordedAtUtc,
    status: view.status as StockCountStatus,
    decision,
  }
}

export function decodeLabelStockCounts(value: unknown): LabelStockCounts | null {
  const counts = record(value)
  if (counts === null || !exact(counts, ['labelId', 'items', 'windowFull']) || !isGuid(counts.labelId) ||
    typeof counts.windowFull !== 'boolean' || !Array.isArray(counts.items)) return null
  const items = counts.items.map(decodeStockCountView)
  if (items.some((item) => item === null)) return null
  const typed = items as StockCountView[]
  const labelId = counts.labelId.toLowerCase()
  if (typed.some((item) => item.labelId.toLowerCase() !== labelId)) return null
  return { labelId: counts.labelId, items: typed, windowFull: counts.windowFull }
}

export function isStockCountRequest(value: unknown): value is StockCountRequest {
  const request = record(value)
  return request !== null && exact(request, ['countId', 'labelId', 'expectedLabelVersion', 'countedKg', 'countedAtUtc', 'reason']) &&
    isGuid(request.countId) && isGuid(request.labelId) && isVersion(request.expectedLabelVersion) &&
    isCountedKilogramText(request.countedKg) && isUtcInstant(request.countedAtUtc) && isReason(request.reason)
}

/** A response is shown only when it is the answer to THIS count in THIS company. */
export function describesRecordedCount(view: StockCountView, request: StockCountRequest, companyId: string): boolean {
  return view.companyId.toLowerCase() === companyId.toLowerCase() &&
    view.countId.toLowerCase() === request.countId.toLowerCase() &&
    view.labelId.toLowerCase() === request.labelId.toLowerCase() &&
    view.labelVersion === request.expectedLabelVersion &&
    kilogramsToGrams(view.countedKg) === kilogramsToGrams(request.countedKg) &&
    view.reason === request.reason &&
    Date.parse(view.countedAtUtc) === Date.parse(request.countedAtUtc)
}

/** A decision answer is shown only when it decided THIS count THIS way with THIS note, in THIS company. */
export function describesDecision(
  view: StockCountView,
  countId: string,
  request: StockCountDecisionRequest,
  companyId: string,
): boolean {
  return view.companyId.toLowerCase() === companyId.toLowerCase() &&
    view.countId.toLowerCase() === countId.toLowerCase() &&
    view.decision !== null && view.decision.reason === request.reason &&
    view.decision.decision === (request.decision === 'Approve' ? 'Approved' : 'Rejected')
}

/**
 * The coded 503s raised BEFORE any write (`InventoryStockCountHttpCodesV1.Status503Codes`): nothing was recorded,
 * so such an answer is a definite failure. A code-less 503, or any other 5xx, stays unknown.
 */
export const STOCK_COUNT_DEFINITE_UNAVAILABLE_CODES: ReadonlySet<string> = new Set([
  'inventory_permission_unregistered',
  'inventory_stock_count_unavailable',
  'inventory_kg_profile_unusable',
  'settings_provider_unavailable',
  'settings_unit_profile_not_found',
  'settings_profile_invalid',
  'settings_access_denied',
  'settings_decimal_invalid',
  'settings_decimal_scale_exceeded',
])

const REFUSAL_TEXT: Readonly<Record<string, string>> = {
  validation_failed: 'İstek geçersiz: sayılan kg (en fazla 3 ondalık, negatif olamaz) veya gerekçe kurallara uymuyor.',
  idempotency_conflict: 'Bu gönderim anahtarı başka bir içerikle kullanılmış. Etiketi yeniden açıp tekrar deneyin.',
  inventory_label_not_found: 'Etiket bu şirkette bulunamadı.',
  inventory_stock_count_not_found: 'Sayım bu şirkette bulunamadı.',
  inventory_label_version_conflict: 'Etiket siz sayımı girerken değişti; defter miktarı artık farklı olabilir. Etiketi yeniden açıp tekrar sayın.',
  inventory_stock_count_position_conflict: 'Sayım için etiketin tek bir fiziksel pozisyonda stoğu olmalı.',
  inventory_stock_count_conflict: 'Bu sayım kimliği farklı bir içerikle kayıtlı.',
  inventory_stock_count_not_pending: 'Bu sayımda onaylanacak fark yok.',
  inventory_stock_count_already_decided: 'Bu sayım zaten başka bir kararla sonuçlandırılmış.',
  inventory_stock_count_self_approval: 'Kendi girdiğiniz sayım farkını onaylayamaz veya reddedemezsiniz; karar başka bir yetkili tarafından verilmelidir.',
  inventory_stock_count_stale: 'Sayımdan sonra etiketin stoğu değişti; bu sayım onaylanamaz. Reddedip yeniden sayın.',
  inventory_stock_count_allocation_conflict: 'Sayılan miktar etiketteki aktif tahsislerden az; önce tahsislerin serbest bırakılması gerekir.',
  inventory_concurrent_change: 'Aynı etiket eşzamanlı değişti. Etiketi yeniden açıp tekrar deneyin.',
  inventory_kg_scale_exceeded: 'Kg değeri şirketin onaylı kg ondalık hassasiyetini aşıyor.',
  forbidden: 'Bu şirkette bu sayım işlemi için yetkiniz yok.',
  inventory_permission_unregistered: 'Stok sayımı bu sunucuda henüz yayımlanmadı; hiçbir şey kaydedilmedi.',
  inventory_stock_count_unavailable: 'Sayımın kayıtlı durumu okunamadı; hiçbir şey kaydedilmedi.',
  inventory_kg_profile_unusable: 'Şirketin onaylı kg profili bu tarih için kullanılamıyor; hiçbir şey kaydedilmedi.',
  unauthenticated: 'Oturum doğrulanamadı.',
}

export function stockCountRefusalText(status: number, code: string | null): string {
  if (code !== null && REFUSAL_TEXT[code] !== undefined) return REFUSAL_TEXT[code]
  if (status === 403) return REFUSAL_TEXT.forbidden
  if (status === 401) return REFUSAL_TEXT.unauthenticated
  if (status === 503) return `Stok sayımı şu an kullanılamıyor (${code ?? status}); hiçbir şey kaydedilmedi.`
  return `Stok sayımı kaydedilmedi (${code ?? status}).`
}

export const STOCK_COUNT_STATUS_TEXT: Readonly<Record<StockCountStatus, string>> = {
  NoDifference: 'Fark yok',
  PendingApproval: 'Onay bekliyor',
  Approved: 'Onaylandı (stoğa işlendi)',
  Rejected: 'Reddedildi',
}

export { isReason }

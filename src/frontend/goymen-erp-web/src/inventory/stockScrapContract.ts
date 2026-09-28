/**
 * ST-02 scrap write-off (hurda/fire çıkışı) — the stock-count (AT-037) pattern.
 *
 * Owner contract (`InventoryStockScrapApiV1`):
 * - `POST /api/v1/inventory/stock-scraps` (`RecordInventoryStockScrapV1` → `InventoryStockScrapViewV1`)
 * - `POST /api/v1/inventory/stock-scraps/{scrapId}/decision` (`DecideInventoryStockScrapV1` → view)
 * - `GET /api/v1/inventory/labels/{labelId}/stock-scraps` (`InventoryLabelStockScrapsV1`)
 *
 * A recorded scrap changes no stock; it leaves stock only after another actor approves it (one Scrap movement). The
 * server decides the book, the position and every conflict; this module only builds requests the writer can accept
 * and refuses to show a response that does not describe that request.
 */
import { gramsToKilograms, isGuid, isReason, KG_MAX_DECIMALS, kilogramsToGrams, REASON_MAX_LENGTH } from './labelTransformationContract'

export { REASON_MAX_LENGTH }

export type StockScrapStatus = 'PendingApproval' | 'Approved' | 'Rejected'
export type StockScrapDecisionKind = 'Approve' | 'Reject'

export interface StockScrapRequest {
  readonly scrapId: string
  readonly labelId: string
  readonly expectedLabelVersion: string
  readonly scrapKg: string
  readonly scrappedAtUtc: string
  readonly reason: string
}

export interface StockScrapDecisionRequest {
  readonly decision: StockScrapDecisionKind
  readonly reason: string
}

export interface StockScrapDecisionView {
  readonly decision: 'Approved' | 'Rejected'
  readonly decidedAtUtc: string
  readonly reason: string
  readonly scrapMovementId: string | null
}

export interface StockScrapView {
  readonly scrapId: string
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
  readonly scrapKg: string
  readonly remainingKg: string
  readonly scrappedAtUtc: string
  readonly reason: string
  readonly recordedAtUtc: string
  readonly status: StockScrapStatus
  readonly decision: StockScrapDecisionView | null
}

export interface LabelStockScraps {
  readonly labelId: string
  readonly items: readonly StockScrapView[]
  readonly windowFull: boolean
}

type JsonObject = Record<string, unknown>

const NON_NEGATIVE = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
const VERSION = /^[1-9]\d*$/
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$/
const STATUSES: readonly StockScrapStatus[] = ['PendingApproval', 'Approved', 'Rejected']

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

function isNonNegativeKilogramText(value: unknown): value is string {
  return typeof value === 'string' && NON_NEGATIVE.test(value) && decimals(value) <= KG_MAX_DECIMALS
}

/** A scrap kg: canonical, positive, at most three decimals. */
export function isScrapKilogramText(value: unknown): value is string {
  return isNonNegativeKilogramText(value) && /[1-9]/.test(value)
}

/** book − scrap as the server writes it, or null when the scrap is more than the book. */
export function remainingKilograms(bookKg: string, scrapKg: string): string | null {
  const remaining = kilogramsToGrams(bookKg) - kilogramsToGrams(scrapKg)
  return remaining < 0n ? null : gramsToKilograms(remaining)
}

function decodeDecision(value: unknown): StockScrapDecisionView | null {
  const decision = record(value)
  if (decision === null || !exact(decision, ['decision', 'decidedAtUtc', 'reason', 'scrapMovementId'])) return null
  if ((decision.decision !== 'Approved' && decision.decision !== 'Rejected') || !isUtcInstant(decision.decidedAtUtc) ||
    typeof decision.reason !== 'string' || decision.reason.trim().length === 0) return null
  const movement = decision.scrapMovementId
  if (decision.decision === 'Approved' ? !isGuid(movement) : movement !== null) return null
  return {
    decision: decision.decision,
    decidedAtUtc: decision.decidedAtUtc,
    reason: decision.reason,
    scrapMovementId: movement as string | null,
  }
}

const VIEW_FIELDS = [
  'scrapId', 'companyId', 'labelId', 'labelVersion', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId',
  'locationId', 'productId', 'block', 'bookKg', 'scrapKg', 'remainingKg', 'scrappedAtUtc', 'reason',
  'recordedAtUtc', 'status', 'decision',
] as const

/** One scrap, only when it is internally consistent: remaining = book − scrap and the status matches the decision. */
export function decodeStockScrapView(value: unknown): StockScrapView | null {
  const view = record(value)
  if (view === null || !exact(view, VIEW_FIELDS)) return null
  if (!isGuid(view.scrapId) || !isGuid(view.companyId) || !isGuid(view.labelId) || !isVersion(view.labelVersion) ||
    typeof view.ownerKind !== 'string' || !isGuid(view.ownerId) || typeof view.custodianKind !== 'string' ||
    !isGuid(view.custodianId) || !isGuid(view.locationId) || !isGuid(view.productId) ||
    (view.block !== 'Free' && view.block !== 'Blocked') || !isScrapKilogramText(view.bookKg) ||
    !isScrapKilogramText(view.scrapKg) || !isNonNegativeKilogramText(view.remainingKg) ||
    !isUtcInstant(view.scrappedAtUtc) || !isUtcInstant(view.recordedAtUtc) ||
    typeof view.reason !== 'string' || view.reason.trim().length === 0 ||
    !STATUSES.includes(view.status as StockScrapStatus)) return null
  if (remainingKilograms(view.bookKg, view.scrapKg) !== view.remainingKg) return null
  const decision = view.decision === null ? null : decodeDecision(view.decision)
  if (view.decision !== null && decision === null) return null
  const consistent = view.status === 'PendingApproval' ? decision === null
    : decision !== null && decision.decision === view.status
  if (!consistent) return null
  return {
    scrapId: view.scrapId,
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
    scrapKg: view.scrapKg,
    remainingKg: view.remainingKg,
    scrappedAtUtc: view.scrappedAtUtc,
    reason: view.reason,
    recordedAtUtc: view.recordedAtUtc,
    status: view.status as StockScrapStatus,
    decision,
  }
}

export function decodeLabelStockScraps(value: unknown): LabelStockScraps | null {
  const scraps = record(value)
  if (scraps === null || !exact(scraps, ['labelId', 'items', 'windowFull']) || !isGuid(scraps.labelId) ||
    typeof scraps.windowFull !== 'boolean' || !Array.isArray(scraps.items)) return null
  const items = scraps.items.map(decodeStockScrapView)
  if (items.some((item) => item === null)) return null
  const typed = items as StockScrapView[]
  const labelId = scraps.labelId.toLowerCase()
  if (typed.some((item) => item.labelId.toLowerCase() !== labelId)) return null
  return { labelId: scraps.labelId, items: typed, windowFull: scraps.windowFull }
}

export function isStockScrapRequest(value: unknown): value is StockScrapRequest {
  const request = record(value)
  return request !== null && exact(request, ['scrapId', 'labelId', 'expectedLabelVersion', 'scrapKg', 'scrappedAtUtc', 'reason']) &&
    isGuid(request.scrapId) && isGuid(request.labelId) && isVersion(request.expectedLabelVersion) &&
    isScrapKilogramText(request.scrapKg) && isUtcInstant(request.scrappedAtUtc) && isReason(request.reason)
}

/** A response is shown only when it is the answer to THIS scrap in THIS company. */
export function describesRecordedScrap(view: StockScrapView, request: StockScrapRequest, companyId: string): boolean {
  return view.companyId.toLowerCase() === companyId.toLowerCase() &&
    view.scrapId.toLowerCase() === request.scrapId.toLowerCase() &&
    view.labelId.toLowerCase() === request.labelId.toLowerCase() &&
    view.labelVersion === request.expectedLabelVersion &&
    kilogramsToGrams(view.scrapKg) === kilogramsToGrams(request.scrapKg) &&
    view.reason === request.reason &&
    Date.parse(view.scrappedAtUtc) === Date.parse(request.scrappedAtUtc)
}

/** A decision answer is shown only when it decided THIS scrap THIS way with THIS note, in THIS company. */
export function describesScrapDecision(
  view: StockScrapView,
  scrapId: string,
  request: StockScrapDecisionRequest,
  companyId: string,
): boolean {
  return view.companyId.toLowerCase() === companyId.toLowerCase() &&
    view.scrapId.toLowerCase() === scrapId.toLowerCase() &&
    view.decision !== null && view.decision.reason === request.reason &&
    view.decision.decision === (request.decision === 'Approve' ? 'Approved' : 'Rejected')
}

/**
 * The coded 503s raised BEFORE any write (`InventoryStockScrapHttpCodesV1.Status503Codes`): nothing was recorded,
 * so such an answer is a definite failure. A code-less 503, or any other 5xx, stays unknown.
 */
export const STOCK_SCRAP_DEFINITE_UNAVAILABLE_CODES: ReadonlySet<string> = new Set([
  'inventory_permission_unregistered',
  'inventory_stock_scrap_unavailable',
  'inventory_kg_profile_unusable',
  'settings_provider_unavailable',
  'settings_unit_profile_not_found',
  'settings_profile_invalid',
  'settings_access_denied',
  'settings_decimal_invalid',
  'settings_decimal_scale_exceeded',
])

const REFUSAL_TEXT: Readonly<Record<string, string>> = {
  validation_failed: 'İstek geçersiz: hurda kg (sıfırdan büyük, en fazla 3 ondalık) veya gerekçe kurallara uymuyor.',
  idempotency_conflict: 'Bu gönderim anahtarı başka bir içerikle kullanılmış. Etiketi yeniden açıp tekrar deneyin.',
  inventory_label_not_found: 'Etiket bu şirkette bulunamadı.',
  inventory_stock_scrap_not_found: 'Hurda kaydı bu şirkette bulunamadı.',
  inventory_label_version_conflict: 'Etiket siz hurdayı girerken değişti; defter miktarı artık farklı olabilir. Etiketi yeniden açıp tekrar deneyin.',
  inventory_stock_scrap_position_conflict: 'Hurda için etiketin tek bir fiziksel pozisyonda stoğu olmalı.',
  inventory_stock_scrap_exceeds_book: 'Hurda miktarı etiketin defter miktarından fazla olamaz.',
  inventory_stock_scrap_not_own_stock: 'Bu etiket şirketin kendi gözetimindeki kendi malı değil (müşteri malı veya fasoncudaki mal); hurda çıkışı bu stok için tanımlı değil.',
  inventory_stock_scrap_conflict: 'Bu hurda kimliği farklı bir içerikle kayıtlı.',
  inventory_stock_scrap_already_decided: 'Bu hurda kaydı zaten başka bir kararla sonuçlandırılmış.',
  inventory_stock_scrap_self_approval: 'Kendi girdiğiniz hurdayı onaylayamaz veya reddedemezsiniz; karar başka bir yetkili tarafından verilmelidir.',
  inventory_stock_scrap_stale: 'Hurda girildikten sonra etiketin stoğu değişti; bu kayıt onaylanamaz. Reddedip yeniden girin.',
  inventory_stock_scrap_allocation_conflict: 'Hurdadan sonra kalan miktar etiketteki aktif tahsislerden az olur; önce tahsislerin serbest bırakılması gerekir.',
  inventory_concurrent_change: 'Aynı etiket eşzamanlı değişti. Etiketi yeniden açıp tekrar deneyin.',
  inventory_kg_scale_exceeded: 'Kg değeri şirketin onaylı kg ondalık hassasiyetini aşıyor.',
  forbidden: 'Bu şirkette bu hurda işlemi için yetkiniz yok.',
  inventory_permission_unregistered: 'Hurda çıkışı bu sunucuda henüz yayımlanmadı; hiçbir şey kaydedilmedi.',
  inventory_stock_scrap_unavailable: 'Hurda kaydının durumu okunamadı; hiçbir şey kaydedilmedi.',
  inventory_kg_profile_unusable: 'Şirketin onaylı kg profili bu tarih için kullanılamıyor; hiçbir şey kaydedilmedi.',
  unauthenticated: 'Oturum doğrulanamadı.',
}

export function stockScrapRefusalText(status: number, code: string | null): string {
  if (code !== null && REFUSAL_TEXT[code] !== undefined) return REFUSAL_TEXT[code]
  if (status === 403) return REFUSAL_TEXT.forbidden
  if (status === 401) return REFUSAL_TEXT.unauthenticated
  if (status === 503) return `Hurda çıkışı şu an kullanılamıyor (${code ?? status}); hiçbir şey kaydedilmedi.`
  return `Hurda kaydedilmedi (${code ?? status}).`
}

export const STOCK_SCRAP_STATUS_TEXT: Readonly<Record<StockScrapStatus, string>> = {
  PendingApproval: 'Onay bekliyor',
  Approved: 'Onaylandı (stoktan düşüldü)',
  Rejected: 'Reddedildi',
}

export { isReason }

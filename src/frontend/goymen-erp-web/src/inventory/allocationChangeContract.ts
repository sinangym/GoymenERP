/**
 * INV-AL-1 change — revise an existing physical sales allocation from the ST-02 label detail (Master v3 §25.4
 * "Tahsisi görüntüle/revize et/serbest bırak", AT-063 "Tahsis Revize").
 *
 * Owner contract:
 * - `GET /api/v1/inventory/labels/{labelId}/physical-allocations` (`InventoryLabelPhysicalAllocationsV1`)
 * - `POST /api/v1/inventory/physical-allocations/{allocationId}/change`
 *   (`ChangeInventoryPhysicalAllocationRequestV1` → `InventoryPhysicalAllocationChangeViewV1`)
 * - `POST /api/v1/inventory/physical-allocations/{allocationId}/release`
 *   (`ReleasePhysicalAllocationV1` → `InventoryPhysicalAllocationViewV1`) — the EXISTING idempotent release command,
 *   same `inventory.physical-allocation.write` permission; it frees the whole remaining reservation, keeps the row
 *   (status Released, reason and time) and writes no stock movement.
 *
 * A change to the allocation's own label re-states its kilograms in place (same allocation, the consumed part kept);
 * a change to another label of the same product releases it and reserves the other label for the same order line.
 * The server decides free stock, ownership, versions and the approved Sales line; this module only builds requests
 * the writer can accept and refuses to show an answer that does not describe the request.
 */
import { isGuid, isReason, KG_MAX_DECIMALS, kilogramsToGrams, REASON_MAX_LENGTH } from './labelTransformationContract'

export { REASON_MAX_LENGTH, isReason }

export type AllocationStatus = 'Active' | 'Consumed' | 'Released'
export type AllocationChangeKind = 'Resize' | 'Move'

export interface LabelAllocation {
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
}

export interface AllocationChangeEntry {
  readonly changeId: string
  readonly kind: AllocationChangeKind
  readonly sourceAllocationId: string
  readonly sourceVersion: string
  readonly sourceLabelId: string
  readonly sourceRemainingKg: string
  readonly targetAllocationId: string
  readonly targetLabelId: string
  readonly targetRemainingKg: string
  readonly reason: string
  readonly changedAtUtc: string
}

export interface LabelAllocationItem {
  readonly allocation: LabelAllocation
  readonly releasedAtUtc: string | null
  readonly releaseReason: string | null
  readonly changes: readonly AllocationChangeEntry[]
}

export interface LabelAllocations {
  readonly labelId: string
  readonly labelVersion: string
  readonly items: readonly LabelAllocationItem[]
  readonly windowFull: boolean
}

export interface AllocationChangeRequest {
  readonly expectedAllocationVersion: string
  readonly salesOrderId: string
  readonly salesOrderLineId: string
  readonly targetLabelId: string
  readonly expectedTargetLabelVersion: string
  readonly quantityKg: string
  readonly reason: string
  readonly newAllocationId: string | null
}

/** The release body: the allocation version the user saw and a mandatory reason (≤ 1000 characters). */
export interface AllocationReleaseRequest {
  readonly expectedAllocationVersion: string
  readonly reason: string
}

export interface AllocationChangeView {
  readonly kind: AllocationChangeKind
  readonly allocation: LabelAllocation
  readonly replaced: LabelAllocation | null
}

type JsonObject = Record<string, unknown>

const NON_NEGATIVE = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
const VERSION = /^[1-9]\d*$/
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$/
const STATUSES: readonly AllocationStatus[] = ['Active', 'Consumed', 'Released']
const KINDS: readonly AllocationChangeKind[] = ['Resize', 'Move']

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

function isKilogramText(value: unknown): value is string {
  return typeof value === 'string' && NON_NEGATIVE.test(value) && (value.split('.')[1]?.length ?? 0) <= KG_MAX_DECIMALS
}

/** A kilogram quantity the change may name: canonical, positive, at most three decimals. */
export function isPositiveKilogramText(value: unknown): value is string {
  return isKilogramText(value) && /[1-9]/.test(value)
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

const ALLOCATION_FIELDS = [
  'allocationId', 'companyId', 'version', 'status', 'labelId', 'productId', 'demandKind', 'demandId', 'demandLineId',
  'demandProductVersion', 'allocatedKg', 'remainingKg', 'reason', 'reservedUntilUtc', 'createdAtUtc', 'updatedAtUtc',
] as const

/** One allocation, only when internally consistent (an active one holds kg, a closed one holds none). */
export function decodeLabelAllocation(value: unknown): LabelAllocation | null {
  const source = record(value)
  if (source === null || !exact(source, ALLOCATION_FIELDS)) return null
  if (!isGuid(source.allocationId) || !isGuid(source.companyId) || !isVersion(source.version) ||
    !STATUSES.includes(source.status as AllocationStatus) || !isGuid(source.labelId) || !isGuid(source.productId) ||
    source.demandKind !== 'sales.order-line' || !isGuid(source.demandId) || !isGuid(source.demandLineId) ||
    !text(source.demandProductVersion) || !isPositiveKilogramText(source.allocatedKg) ||
    !isKilogramText(source.remainingKg) || !text(source.reason) ||
    !(source.reservedUntilUtc === null || isUtcInstant(source.reservedUntilUtc)) ||
    !isUtcInstant(source.createdAtUtc) || !isUtcInstant(source.updatedAtUtc)) return null
  if (kilogramsToGrams(source.remainingKg) > kilogramsToGrams(source.allocatedKg)) return null
  if (source.status === 'Active' ? source.remainingKg === '0' : source.remainingKg !== '0') return null
  return source as unknown as LabelAllocation
}

const CHANGE_FIELDS = [
  'changeId', 'kind', 'sourceAllocationId', 'sourceVersion', 'sourceLabelId', 'sourceRemainingKg',
  'targetAllocationId', 'targetLabelId', 'targetRemainingKg', 'reason', 'changedAtUtc',
] as const

function decodeChangeEntry(value: unknown): AllocationChangeEntry | null {
  const source = record(value)
  if (source === null || !exact(source, CHANGE_FIELDS)) return null
  if (!isGuid(source.changeId) || !KINDS.includes(source.kind as AllocationChangeKind) ||
    !isGuid(source.sourceAllocationId) || !isVersion(source.sourceVersion) || !isGuid(source.sourceLabelId) ||
    !isPositiveKilogramText(source.sourceRemainingKg) || !isGuid(source.targetAllocationId) ||
    !isGuid(source.targetLabelId) || !isPositiveKilogramText(source.targetRemainingKg) || !text(source.reason) ||
    !isUtcInstant(source.changedAtUtc)) return null
  const sameAllocation = source.sourceAllocationId.toLowerCase() === source.targetAllocationId.toLowerCase()
  const sameLabel = source.sourceLabelId.toLowerCase() === source.targetLabelId.toLowerCase()
  if (source.kind === 'Resize' ? !sameAllocation || !sameLabel : sameAllocation || sameLabel) return null
  return source as unknown as AllocationChangeEntry
}

function decodeItem(value: unknown): LabelAllocationItem | null {
  const item = record(value)
  if (item === null || !exact(item, ['allocation', 'releasedAtUtc', 'releaseReason', 'changes']) ||
    !Array.isArray(item.changes)) return null
  const allocation = decodeLabelAllocation(item.allocation)
  if (allocation === null) return null
  const released = allocation.status === 'Released'
  if (released ? !isUtcInstant(item.releasedAtUtc) || !text(item.releaseReason)
    : item.releasedAtUtc !== null || item.releaseReason !== null) return null
  const changes = item.changes.map(decodeChangeEntry)
  if (changes.some((change) => change === null)) return null
  const id = allocation.allocationId.toLowerCase()
  const typed = changes as AllocationChangeEntry[]
  if (typed.some((change) => change.sourceAllocationId.toLowerCase() !== id && change.targetAllocationId.toLowerCase() !== id)) return null
  return {
    allocation,
    releasedAtUtc: item.releasedAtUtc as string | null,
    releaseReason: item.releaseReason as string | null,
    changes: typed,
  }
}

export function decodeLabelAllocations(value: unknown): LabelAllocations | null {
  const source = record(value)
  if (source === null || !exact(source, ['labelId', 'labelVersion', 'items', 'windowFull']) || !isGuid(source.labelId) ||
    !isVersion(source.labelVersion) || typeof source.windowFull !== 'boolean' || !Array.isArray(source.items)) return null
  const items = source.items.map(decodeItem)
  if (items.some((item) => item === null)) return null
  const typed = items as LabelAllocationItem[]
  const labelId = source.labelId.toLowerCase()
  const ids = new Set(typed.map((item) => item.allocation.allocationId.toLowerCase()))
  if (ids.size !== typed.length || typed.some((item) => item.allocation.labelId.toLowerCase() !== labelId)) return null
  return { labelId: source.labelId, labelVersion: source.labelVersion, items: typed, windowFull: source.windowFull }
}

export function decodeAllocationChangeView(value: unknown): AllocationChangeView | null {
  const source = record(value)
  if (source === null || !exact(source, ['kind', 'allocation', 'replaced']) ||
    !KINDS.includes(source.kind as AllocationChangeKind)) return null
  const allocation = decodeLabelAllocation(source.allocation)
  const replaced = source.replaced === null ? null : decodeLabelAllocation(source.replaced)
  if (allocation === null || allocation.status !== 'Active') return null
  if (source.kind === 'Resize' ? source.replaced !== null : replaced === null || replaced.status !== 'Released') return null
  return { kind: source.kind as AllocationChangeKind, allocation, replaced }
}

/** An answer is shown only when it is the change of THIS allocation to THIS label and kg, in THIS company. */
export function describesChange(
  view: AllocationChangeView,
  allocationId: string,
  request: AllocationChangeRequest,
  companyId: string,
): boolean {
  const current = view.allocation
  const sameDemand = current.demandId.toLowerCase() === request.salesOrderId.toLowerCase() &&
    current.demandLineId.toLowerCase() === request.salesOrderLineId.toLowerCase()
  const common = current.companyId.toLowerCase() === companyId.toLowerCase() && sameDemand &&
    current.labelId.toLowerCase() === request.targetLabelId.toLowerCase() &&
    kilogramsToGrams(current.remainingKg) === kilogramsToGrams(request.quantityKg)
  if (!common) return false
  if (view.kind === 'Resize') {
    return request.newAllocationId === null && current.allocationId.toLowerCase() === allocationId.toLowerCase()
  }
  return request.newAllocationId !== null && view.replaced !== null &&
    current.allocationId.toLowerCase() === request.newAllocationId.toLowerCase() &&
    view.replaced.allocationId.toLowerCase() === allocationId.toLowerCase()
}

/** A release answer is shown only when it is THIS allocation, in THIS company, now Released and holding no kg. */
export function describesRelease(view: LabelAllocation, allocationId: string, companyId: string): boolean {
  return view.allocationId.toLowerCase() === allocationId.toLowerCase() &&
    view.companyId.toLowerCase() === companyId.toLowerCase() && view.status === 'Released' && view.remainingKg === '0'
}

const REFUSAL_TEXT: Readonly<Record<string, string>> = {
  validation_failed: 'İstek geçersiz: miktar (pozitif, en fazla 3 ondalık), hedef etiket veya gerekçe kurallara uymuyor.',
  idempotency_conflict: 'Bu gönderim anahtarı başka bir içerikle kullanılmış. Etiketi yeniden açıp tekrar deneyin.',
  inventory_allocation_invalid_reference:
    'Tahsis bu işlem için kullanılamıyor: sipariş satırı artık onaylı değil (iptal edilmiş olabilir), hedef etiket aynı ürünün değil veya tahsis bu satıra ait değil. Gerekirse tahsisi serbest bırakın.',
  inventory_label_version_conflict: 'Hedef etiket siz bakarken değişti. Etiketi yeniden açıp tekrar deneyin.',
  inventory_allocation_version_conflict: 'Tahsis siz bakarken değişti. Etiketi yeniden açıp güncel tahsisi görün.',
  inventory_allocation_state_conflict: 'Tahsis artık aktif değil (serbest bırakılmış veya tüketilmiş); değiştirilemez.',
  inventory_allocation_insufficient_free_stock: 'Hedef etiketin serbest (tahsis edilmemiş) miktarı bu kadar tahsise yetmiyor.',
  inventory_allocation_owner_not_company:
    'Hedef stok şirketin kendi malı ve kendi elinde değil (müşteri malı veya fasonda); satış siparişine tahsis edilemez.',
  inventory_allocation_change_unchanged: 'Değişiklik yok: aynı etiket ve aynı miktar.',
  inventory_concurrent_change: 'Aynı etiket eşzamanlı değişti. Etiketi yeniden açıp tekrar deneyin.',
  forbidden: 'Bu şirkette tahsis değiştirme yetkiniz yok.',
  unauthenticated: 'Oturum doğrulanamadı.',
}

export function allocationChangeRefusalText(status: number, code: string | null): string {
  if (code !== null && REFUSAL_TEXT[code] !== undefined) return REFUSAL_TEXT[code]
  if (status === 403) return REFUSAL_TEXT.forbidden
  if (status === 401) return REFUSAL_TEXT.unauthenticated
  if (status === 404) return 'Tahsis, sipariş satırı veya hedef etiket bu şirkette bulunamadı.'
  return `Tahsis değiştirilmedi (${code ?? status}).`
}

const RELEASE_REFUSAL_TEXT: Readonly<Record<string, string>> = {
  validation_failed: 'İstek geçersiz: serbest bırakma gerekçesi zorunludur (en fazla 1000 karakter).',
  inventory_allocation_invalid_reference: 'Bu tahsis bir satış siparişi satırına ait değil; buradan serbest bırakılamaz.',
  inventory_allocation_state_conflict: 'Tahsis artık aktif değil (serbest bırakılmış veya tüketilmiş); serbest bırakılamaz.',
  forbidden: 'Bu şirkette tahsis serbest bırakma yetkiniz yok.',
}

export function allocationReleaseRefusalText(status: number, code: string | null): string {
  if (code !== null && RELEASE_REFUSAL_TEXT[code] !== undefined) return RELEASE_REFUSAL_TEXT[code]
  if (code !== null && REFUSAL_TEXT[code] !== undefined) return REFUSAL_TEXT[code]
  if (status === 403) return RELEASE_REFUSAL_TEXT.forbidden
  if (status === 401) return REFUSAL_TEXT.unauthenticated
  if (status === 404) return 'Tahsis bu şirkette bulunamadı.'
  return `Tahsis serbest bırakılmadı (${code ?? status}).`
}

export const ALLOCATION_STATUS_TEXT: Readonly<Record<AllocationStatus, string>> = {
  Active: 'Aktif',
  Consumed: 'Tüketildi',
  Released: 'Serbest bırakıldı',
}

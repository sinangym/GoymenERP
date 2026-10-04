export type LabelClassification = 'Standard' | 'SecondQuality' | 'Scrap'

export interface LabelClassificationRequest {
  readonly expectedLabelVersion: string
  readonly classification: LabelClassification
}

export interface LabelClassificationView {
  readonly labelId: string
  readonly labelVersion: string
  readonly classification: LabelClassification
  readonly classifiedAtUtc: string
}

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const VERSION = /^[1-9]\d*$/

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function classification(value: unknown): value is LabelClassification {
  return value === 'Standard' || value === 'SecondQuality' || value === 'Scrap'
}

function version(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 19 && VERSION.test(value)
    && BigInt(value) <= 9223372036854775807n
}

export function isLabelClassificationRequest(value: unknown): value is LabelClassificationRequest {
  const source = record(value)
  return source !== null && exact(source, ['expectedLabelVersion', 'classification'])
    && version(source.expectedLabelVersion) && classification(source.classification)
}

export function decodeLabelClassificationView(value: unknown): LabelClassificationView | null {
  const source = record(value)
  if (source === null || !exact(source, ['labelId', 'labelVersion', 'classification', 'classifiedAtUtc']) ||
    typeof source.labelId !== 'string' || !GUID.test(source.labelId) || !version(source.labelVersion) ||
    !classification(source.classification) || typeof source.classifiedAtUtc !== 'string' ||
    !Number.isFinite(Date.parse(source.classifiedAtUtc))) return null
  return source as unknown as LabelClassificationView
}

export function describesLabelClassification(
  view: LabelClassificationView,
  labelId: string,
  request: LabelClassificationRequest,
): boolean {
  return view.labelId.toLowerCase() === labelId.toLowerCase()
    && view.classification === request.classification
    && BigInt(view.labelVersion) === BigInt(request.expectedLabelVersion) + 1n
}

const REFUSALS: Readonly<Record<string, string>> = {
  inventory_label_classification_validation_failed: 'Sınıflandırma isteği geçersiz.',
  inventory_label_classification_idempotency_conflict: 'Bu gönderim anahtarı başka bir sınıflandırma için kullanılmış.',
  inventory_label_classification_label_not_found: 'Etiket bu şirkette bulunamadı.',
  inventory_label_classification_label_version_conflict: 'Etiket değişti; güncel kaydı açıp tekrar deneyin.',
  inventory_label_classification_unchanged: 'Etiket zaten bu sınıfta.',
  inventory_label_classification_concurrent_change: 'Etiket eşzamanlı değişti; güncel kaydı açıp tekrar deneyin.',
  inventory_label_classification_forbidden: 'Bu şirkette etiket sınıflandırma yetkiniz yok.',
  inventory_label_classification_unauthenticated: 'Oturum doğrulanamadı.',
  inventory_label_classification_permission_unregistered: 'Etiket sınıflandırma bu sunucuda henüz yayımlanmadı.',
}

export function labelClassificationRefusal(status: number, code: string | null): string {
  if (code !== null && REFUSALS[code] !== undefined) return REFUSALS[code]
  if (status === 401) return REFUSALS.inventory_label_classification_unauthenticated
  if (status === 403) return REFUSALS.inventory_label_classification_forbidden
  return `Etiket sınıflandırılmadı (${code ?? status}).`
}

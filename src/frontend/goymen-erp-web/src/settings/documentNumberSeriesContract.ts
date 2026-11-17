export interface NumberFormatVersion { readonly version: string; readonly prefix: string; readonly sequenceDigits: number; readonly recordedBy: string; readonly recordedAtUtc: string; readonly reason: string }
export interface NumberFormat { readonly companyId: string; readonly series: number; readonly versions: readonly NumberFormatVersion[] }
export interface NumberGap { readonly series: number; readonly year: number; readonly sequence: string; readonly number: string; readonly documentId: string; readonly cancelledAtUtc: string; readonly reason: string }
export interface RecordNumberFormat { readonly series: number; readonly expectedVersion: string; readonly prefix: string; readonly sequenceDigits: number; readonly reason: string }
export const NUMBER_SERIES_PATH = '/settings/company-policies/document-number-series'
const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, keys: readonly string[]) => Object.keys(raw).length === keys.length && keys.every((key) => Object.hasOwn(raw, key))
const text = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const version = (raw: unknown): raw is string => typeof raw === 'string' && /^[1-9]\d{0,18}$/.test(raw) && BigInt(raw) <= 9223372036854775807n
const timestamp = (raw: unknown): raw is string => typeof raw === 'string' && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(raw) && Number.isFinite(Date.parse(raw))
export function decodeNumberFormat(raw: unknown, company: string, series: number): NumberFormat | null {
  if (!object(raw) || !exact(raw, ['companyId', 'series', 'versions']) || raw.companyId !== company || raw.series !== series || !Array.isArray(raw.versions)) return null
  const items: NumberFormatVersion[] = []
  for (const item of raw.versions) {
    if (!object(item) || !exact(item, ['version', 'prefix', 'sequenceDigits', 'recordedBy', 'recordedAtUtc', 'reason']) || !version(item.version)
      || typeof item.prefix !== 'string' || !/^[A-Z0-9]{1,16}$/.test(item.prefix) || typeof item.sequenceDigits !== 'number' || !Number.isInteger(item.sequenceDigits) || item.sequenceDigits < 1 || item.sequenceDigits > 18
      || !text(item.recordedBy) || !timestamp(item.recordedAtUtc) || !text(item.reason) || (items.length > 0 && BigInt(items[items.length - 1].version) <= BigInt(item.version))) return null
    items.push({ version: item.version, prefix: item.prefix, sequenceDigits: item.sequenceDigits, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc, reason: item.reason })
  }
  return { companyId: company, series, versions: items }
}
export function decodeNumberGaps(raw: unknown, series: number, year: number): readonly NumberGap[] | null {
  if (!Array.isArray(raw)) return null
  const items: NumberGap[] = []
  for (const item of raw) {
    if (!object(item) || !exact(item, ['series', 'year', 'sequence', 'number', 'documentId', 'cancelledAtUtc', 'reason']) || item.series !== series || item.year !== year
      || !version(item.sequence) || (items.length > 0 && BigInt(items[items.length - 1].sequence) >= BigInt(item.sequence))
      || !text(item.number) || typeof item.documentId !== 'string' || !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(item.documentId) || !timestamp(item.cancelledAtUtc) || !text(item.reason)) return null
    items.push({ series, year, sequence: item.sequence, number: item.number, documentId: item.documentId, cancelledAtUtc: item.cancelledAtUtc, reason: item.reason })
  }
  return items
}

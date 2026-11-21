/** KIM-D53 / Q7: opaque server-produced number proof. No client counter or year calculation. */
export interface FinanceDocumentNumber { readonly number: string; readonly formatVersion: string }
export const FINANCE_DOCUMENT_NUMBER_TEXT = {
  receipt: 'Makbuz numarası', voucher: 'Dekont numarası', format: 'Numara biçimi sürümü',
  legacy: 'Bu eski kayıtta belge numarası bulunmuyor.', notRecorded: 'Kesin kayıtta ayrılır.',
} as const
export function decodeFinanceDocumentNumber(value: unknown): FinanceDocumentNumber | undefined | null {
  if (value === undefined) return undefined
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const item = value as Record<string, unknown>
  if (Object.keys(item).length !== 2 || typeof item.number !== 'string' || item.number.length > 64
    || !/^[A-Z0-9]{1,16}-[0-9]{4}-[0-9]{1,19}$/.test(item.number)
    || typeof item.formatVersion !== 'string' || !/^[1-9][0-9]{0,18}$/.test(item.formatVersion)
    || BigInt(item.formatVersion) > 9223372036854775807n) return null
  return { number: item.number, formatVersion: item.formatVersion }
}

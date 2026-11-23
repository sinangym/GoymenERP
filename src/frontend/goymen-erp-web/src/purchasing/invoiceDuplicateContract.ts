/**
 * WP15 (DALGA_PLANI_v2 1.16) — `GET /api/v1/purchasing/invoices/{id}/duplicates`: the rule-based duplicate check of a recorded
 * incoming invoice. The answer is always a warning (`behavior: "warn"`); nothing is blocked (ALS-D125; the record command asks for a reason). The rules
 * run on the server; the screen only lists the matches.
 */

export const INVOICE_DUPLICATE_CODES = [
  'purchasing.invoice-duplicate.same-ettn',
  'purchasing.invoice-duplicate.same-supplier-number',
  'purchasing.invoice-duplicate.same-supplier-date-amount',
] as const
export type InvoiceDuplicateCode = typeof INVOICE_DUPLICATE_CODES[number]

export interface InvoiceDuplicateWarning {
  readonly invoiceId: string
  readonly code: InvoiceDuplicateCode
  readonly officialNumber: string
  readonly invoiceDate: string | null
  readonly link: string
}

export interface InvoiceDuplicateCheck {
  readonly behavior: 'warn'
  readonly warnings: readonly InvoiceDuplicateWarning[]
  readonly truncated: boolean
}

const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) =>
  Object.keys(raw).length === names.length && names.every((name) => Object.hasOwn(raw, name))
const guid = (raw: unknown): raw is string =>
  typeof raw === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(raw) && !/^0{8}-(0{4}-){3}0{12}$/.test(raw)
const dateOnly = (raw: unknown): raw is string => {
  if (typeof raw !== 'string') return false
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(raw)
  if (match === null) return false
  const parsed = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])))
  return parsed.getUTCMonth() === Number(match[2]) - 1 && parsed.getUTCDate() === Number(match[3])
}
const isCode = (raw: unknown): raw is InvoiceDuplicateCode =>
  typeof raw === 'string' && (INVOICE_DUPLICATE_CODES as readonly string[]).includes(raw)

const ROOT_KEYS = ['behavior', 'warnings', 'truncated'] as const
const WARNING_KEYS = ['invoiceId', 'code', 'officialNumber', 'invoiceDate', 'link'] as const

/** `self` is the checked invoice: it can never be its own duplicate. */
export function decodeInvoiceDuplicateCheck(raw: unknown, self: string): InvoiceDuplicateCheck | null {
  if (!object(raw) || !exact(raw, ROOT_KEYS) || raw.behavior !== 'warn' || typeof raw.truncated !== 'boolean' || !Array.isArray(raw.warnings)) return null
  const warnings: InvoiceDuplicateWarning[] = []
  for (const item of raw.warnings) {
    if (!object(item) || !exact(item, WARNING_KEYS) || !guid(item.invoiceId) || item.invoiceId.toLowerCase() === self.toLowerCase()
      || !isCode(item.code) || typeof item.officialNumber !== 'string' || item.officialNumber.trim().length === 0
      || (item.invoiceDate !== null && !dateOnly(item.invoiceDate))
      || item.link !== `/satinalma/faturalar/${item.invoiceId.toLowerCase()}`
      || warnings.some((existing) => existing.invoiceId === item.invoiceId)) return null
    warnings.push({ invoiceId: item.invoiceId, code: item.code, officialNumber: item.officialNumber, invoiceDate: item.invoiceDate, link: item.link })
  }
  return { behavior: 'warn', warnings, truncated: raw.truncated }
}

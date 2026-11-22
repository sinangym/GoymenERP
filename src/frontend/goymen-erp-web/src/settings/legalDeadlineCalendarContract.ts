/**
 * WP15 (DALGA_PLANI_v2 1.16) — `GET /api/v1/settings/company-policies/legal-deadline-calendar`: the company's versioned legal
 * deadline calendar. Nothing is seeded: until the financial advisor confirms the values (MALI_MUSAVIR_SORULARI_v1) the
 * calendar is "not defined" (version "0", no entries, no history) and every counter shows "tanımsız".
 */

export const LEGAL_DEADLINE_CALENDAR_PATH = '/settings/company-policies/legal-deadline-calendar'
export const LEGAL_DEADLINE_CALENDAR_VERSIONS_PATH = `${LEGAL_DEADLINE_CALENDAR_PATH}/versions`

/** The closed obligation catalogue (`LegalDeadlineObligationV1`), in server order. */
export const LEGAL_DEADLINE_OBLIGATIONS = [
  'VatReturn', 'WithholdingReturn', 'ELedgerBerat', 'BaBsForms', 'ProvisionalTaxReturn', 'CorporateTaxReturn',
  'StampTaxReturn', 'SocialSecurityReturn',
] as const
export type LegalDeadlineObligation = typeof LEGAL_DEADLINE_OBLIGATIONS[number]

/** The server's input ranges (`SettingsLegalDeadlineCalendarLimitsV1`); input bounds only, never a deadline value. */
export const LEGAL_DEADLINE_LIMITS = { periodMonths: [1, 3, 12], maximumMonthsAfterPeriodEnd: 12, maximumDueDayOfMonth: 31 } as const

export interface LegalDeadlineEntry {
  readonly obligation: LegalDeadlineObligation
  readonly periodMonths: number
  readonly monthsAfterPeriodEnd: number
  readonly dueDayOfMonth: number
}

export interface LegalDeadlineCalendarVersion {
  readonly version: string
  readonly entryCount: number
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface LegalDeadlineCalendar {
  readonly companyId: string
  readonly isDefined: boolean
  readonly version: string
  readonly entries: readonly LegalDeadlineEntry[]
  readonly versions: readonly LegalDeadlineCalendarVersion[]
}

export interface RecordLegalDeadlineCalendarBody {
  readonly expectedVersion: string
  readonly entries: readonly LegalDeadlineEntry[]
  readonly reason: string
}

const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) =>
  Object.keys(raw).length === names.length && names.every((name) => Object.hasOwn(raw, name))
const nonempty = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const version = (raw: unknown): raw is string =>
  typeof raw === 'string' && /^(0|[1-9]\d{0,18})$/.test(raw) && BigInt(raw) <= 9223372036854775807n
const instant = (raw: unknown): raw is string =>
  typeof raw === 'string' && raw.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(raw) && Number.isFinite(Date.parse(raw))
const integer = (raw: unknown, minimum: number, maximum: number): raw is number =>
  typeof raw === 'number' && Number.isInteger(raw) && raw >= minimum && raw <= maximum
export const isObligation = (raw: unknown): raw is LegalDeadlineObligation =>
  typeof raw === 'string' && (LEGAL_DEADLINE_OBLIGATIONS as readonly string[]).includes(raw)
export const isPeriodMonths = (raw: unknown): raw is number =>
  typeof raw === 'number' && (LEGAL_DEADLINE_LIMITS.periodMonths as readonly number[]).includes(raw)

/** The same range check the server applies; it validates input, it does not compute a deadline. */
export function isValidEntry(entry: LegalDeadlineEntry): boolean {
  return isObligation(entry.obligation) && isPeriodMonths(entry.periodMonths)
    && integer(entry.monthsAfterPeriodEnd, 0, LEGAL_DEADLINE_LIMITS.maximumMonthsAfterPeriodEnd)
    && integer(entry.dueDayOfMonth, 1, LEGAL_DEADLINE_LIMITS.maximumDueDayOfMonth)
}

const ROOT_KEYS = ['companyId', 'isDefined', 'version', 'entries', 'versions'] as const
const ENTRY_KEYS = ['obligation', 'periodMonths', 'monthsAfterPeriodEnd', 'dueDayOfMonth'] as const
const VERSION_KEYS = ['version', 'entryCount', 'recordedBy', 'recordedAtUtc', 'reason'] as const

export function decodeLegalDeadlineCalendar(raw: unknown, company: string): LegalDeadlineCalendar | null {
  if (!object(raw) || !exact(raw, ROOT_KEYS) || typeof raw.companyId !== 'string' || raw.companyId.toLowerCase() !== company.toLowerCase()) return null
  if (typeof raw.isDefined !== 'boolean' || !version(raw.version) || !Array.isArray(raw.entries) || !Array.isArray(raw.versions)) return null
  if (raw.entries.length > LEGAL_DEADLINE_OBLIGATIONS.length) return null
  const entries: LegalDeadlineEntry[] = []
  for (const item of raw.entries) {
    if (!object(item) || !exact(item, ENTRY_KEYS) || !isObligation(item.obligation)) return null
    const entry = { obligation: item.obligation, periodMonths: item.periodMonths, monthsAfterPeriodEnd: item.monthsAfterPeriodEnd, dueDayOfMonth: item.dueDayOfMonth } as LegalDeadlineEntry
    // One entry per obligation, in catalogue order.
    if (!isValidEntry(entry) || (entries.length > 0
      && LEGAL_DEADLINE_OBLIGATIONS.indexOf(entries[entries.length - 1].obligation) >= LEGAL_DEADLINE_OBLIGATIONS.indexOf(entry.obligation))) return null
    entries.push(entry)
  }
  const history: LegalDeadlineCalendarVersion[] = []
  for (const item of raw.versions) {
    if (!object(item) || !exact(item, VERSION_KEYS) || !version(item.version) || item.version === '0'
      || !integer(item.entryCount, 0, LEGAL_DEADLINE_OBLIGATIONS.length)
      || !nonempty(item.recordedBy) || !nonempty(item.reason) || !instant(item.recordedAtUtc)
      || BigInt(item.version) > BigInt(raw.version)
      || (history.length > 0 && BigInt(history[history.length - 1].version) <= BigInt(item.version))) return null
    history.push({ version: item.version, entryCount: item.entryCount, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc, reason: item.reason })
  }
  if (!raw.isDefined ? raw.version !== '0' || entries.length !== 0 || history.length !== 0
    : raw.version === '0' || history[0]?.version !== raw.version || history[0]?.entryCount !== entries.length) return null
  return { companyId: company, isDefined: raw.isDefined, version: raw.version, entries, versions: history }
}

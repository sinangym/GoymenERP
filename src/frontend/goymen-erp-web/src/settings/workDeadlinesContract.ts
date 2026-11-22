/**
 * WP15 (DALGA_PLANI_v2 1.16) — `GET /api/v1/work/deadlines`: Work's read-only "yaklaşan süreler" view. Each owner module lists
 * its deadlines and computes their dates; the screen only shows them. An `undefined` deadline has no date and no counter.
 * A denied source is absent; a failing source is listed as `unavailable`.
 */

export const WORK_DEADLINES_PATH = '/work/deadlines'

export type WorkDeadlineState = 'defined' | 'undefined'
/** YOL-D32: whether the shown due date was moved from the original (legal) date. */
export type WorkDeadlineAdjustment = 'none' | 'moved-to-business-day' | 'holiday-calendar-undefined'
const ADJUSTMENTS: readonly string[] = ['none', 'moved-to-business-day', 'holiday-calendar-undefined']

export interface WorkDeadlineItem {
  readonly module: string
  readonly kind: string
  readonly code: string
  readonly state: WorkDeadlineState
  readonly periodStart: string | null
  readonly periodEnd: string | null
  readonly dueDate: string | null
  readonly daysRemaining: number | null
  readonly definitionVersion: string | null
  readonly link: string
  readonly originalDueDate: string | null
  readonly dueDateAdjustment: WorkDeadlineAdjustment | null
}

export interface WorkDeadlineSource {
  readonly module: string
  readonly kind: string
  readonly outcome: 'found' | 'unavailable'
  readonly hasMore: boolean
}

export interface WorkDeadlinePage {
  readonly companyId: string
  readonly items: readonly WorkDeadlineItem[]
  readonly sources: readonly WorkDeadlineSource[]
}

const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) =>
  Object.keys(raw).length === names.length && names.every((name) => Object.hasOwn(raw, name))
const code = (raw: unknown): raw is string => typeof raw === 'string' && /^[a-z][a-z0-9-]*(\.[a-z0-9-]+)+$/.test(raw) && raw.length <= 200

/** A real calendar day `yyyy-MM-dd`. */
export function isDateOnly(raw: unknown): raw is string {
  if (typeof raw !== 'string') return false
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(raw)
  if (match === null) return false
  const parsed = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])))
  return parsed.getUTCFullYear() === Number(match[1]) && parsed.getUTCMonth() === Number(match[2]) - 1 && parsed.getUTCDate() === Number(match[3])
}

const ROOT_KEYS = ['companyId', 'items', 'sources'] as const
const ITEM_KEYS = ['module', 'kind', 'code', 'state', 'periodStart', 'periodEnd', 'dueDate', 'daysRemaining', 'definitionVersion', 'link',
  'originalDueDate', 'dueDateAdjustment'] as const
const SOURCE_KEYS = ['module', 'kind', 'outcome', 'hasMore'] as const

function decodeItem(raw: unknown): WorkDeadlineItem | null {
  if (!object(raw) || !exact(raw, ITEM_KEYS) || !code(raw.kind) || typeof raw.module !== 'string' || !code(raw.code)
    || !raw.code.startsWith(`${raw.module}.`) || typeof raw.link !== 'string' || !raw.link.startsWith('/')) return null
  const optionalDate = (value: unknown) => value === null || isDateOnly(value)
  if (!optionalDate(raw.periodStart) || !optionalDate(raw.periodEnd) || !optionalDate(raw.dueDate)) return null
  if (raw.state === 'defined') {
    if (!isDateOnly(raw.dueDate) || typeof raw.daysRemaining !== 'number' || !Number.isInteger(raw.daysRemaining) || raw.daysRemaining < 0
      || typeof raw.definitionVersion !== 'string' || raw.definitionVersion.trim().length === 0
      || !isDateOnly(raw.originalDueDate) || typeof raw.dueDateAdjustment !== 'string' || !ADJUSTMENTS.includes(raw.dueDateAdjustment)
      // A moved date lies after its original; otherwise both are the same day (ISO dates compare as text).
      || (raw.dueDateAdjustment === 'moved-to-business-day' ? raw.dueDate <= raw.originalDueDate : raw.dueDate !== raw.originalDueDate)) return null
  } else if (raw.state !== 'undefined' || raw.dueDate !== null || raw.daysRemaining !== null || raw.periodStart !== null
    || raw.periodEnd !== null || raw.definitionVersion !== null || raw.originalDueDate !== null || raw.dueDateAdjustment !== null) return null
  return {
    module: raw.module, kind: raw.kind, code: raw.code, state: raw.state, periodStart: raw.periodStart as string | null,
    periodEnd: raw.periodEnd as string | null, dueDate: raw.dueDate as string | null, daysRemaining: raw.daysRemaining as number | null,
    definitionVersion: raw.definitionVersion as string | null, link: raw.link,
    originalDueDate: raw.originalDueDate as string | null, dueDateAdjustment: raw.dueDateAdjustment as WorkDeadlineAdjustment | null,
  }
}

export function decodeWorkDeadlines(raw: unknown, company: string): WorkDeadlinePage | null {
  if (!object(raw) || !exact(raw, ROOT_KEYS) || typeof raw.companyId !== 'string' || raw.companyId.toLowerCase() !== company.toLowerCase()
    || !Array.isArray(raw.items) || !Array.isArray(raw.sources)) return null
  const sources: WorkDeadlineSource[] = []
  for (const item of raw.sources) {
    if (!object(item) || !exact(item, SOURCE_KEYS) || typeof item.module !== 'string' || !code(item.kind)
      || (item.outcome !== 'found' && item.outcome !== 'unavailable') || typeof item.hasMore !== 'boolean') return null
    sources.push({ module: item.module, kind: item.kind, outcome: item.outcome, hasMore: item.hasMore })
  }
  const items: WorkDeadlineItem[] = []
  for (const item of raw.items) {
    const decoded = decodeItem(item)
    // Every item belongs to a source that answered "found".
    if (decoded === null || !sources.some((source) => source.outcome === 'found' && source.kind === decoded.kind && source.module === decoded.module)) return null
    items.push(decoded)
  }
  return { companyId: company, items, sources }
}

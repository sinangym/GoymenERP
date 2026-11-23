/**
 * WP2 (YOL-D17, YOL-D28): `GET /api/v1/work/approval-follow-ups?view=escalated|orphaned` — Work's own follow-up rows for a
 * holder of `work.approvals.escalate`. Strict decoder: exact keys, anything else is `null` (fail closed, "Beklenmeyen yanıt").
 * The decision is never given here: each item opens the owner's own screen.
 */

export const APPROVAL_FOLLOW_UP_ROUTE = '/onay-takibi'
export const APPROVAL_FOLLOW_UP_PATH = '/work/approval-follow-ups'
export type ApprovalFollowUpView = 'escalated' | 'orphaned'
export const APPROVAL_FOLLOW_UP_VIEWS: readonly ApprovalFollowUpView[] = ['escalated', 'orphaned']

export interface ApprovalFollowUpItem {
  readonly kind: string; readonly itemId: string; readonly documentLabel: string; readonly link: string
  readonly waitingSinceUtc: string; readonly step: ApprovalFollowUpView; readonly approverPermission: string
  readonly escalationPermission: string | null; readonly ruleVersion: string; readonly dueAtUtc: string | null
  readonly recordedAtUtc: string; readonly recipientCount: number
}
export interface ApprovalFollowUpPage {
  readonly companyId: string; readonly view: ApprovalFollowUpView; readonly items: readonly ApprovalFollowUpItem[]; readonly hasMore: boolean
}

export const FOLLOW_UP_ITEM_FIELDS = ['kind', 'itemId', 'documentLabel', 'link', 'waitingSinceUtc', 'step', 'approverPermission',
  'escalationPermission', 'ruleVersion', 'dueAtUtc', 'recordedAtUtc', 'recipientCount'] as const
export const FOLLOW_UP_PAGE_FIELDS = ['companyId', 'view', 'items', 'hasMore'] as const

const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, names: readonly string[]) => Object.keys(value).length === names.length && names.every((name) => Object.hasOwn(value, name))
const guid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const instant = (value: unknown): value is string => typeof value === 'string' && value.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(value) && Number.isFinite(Date.parse(value))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= max
const permission = (value: unknown): value is string => typeof value === 'string' && value.length <= 128 && /^[a-z][a-z0-9-]*(?:\.[a-z0-9-]+)+$/.test(value)
const kind = (value: unknown): value is string => typeof value === 'string' && value.length <= 160 && /^[a-z][a-z0-9-]*(?:\.[a-z0-9-]+)+$/.test(value)
const appLink = (value: unknown): value is string => typeof value === 'string' && value.length <= 512 && /^\/[A-Za-z0-9/_\-?=&.%]*$/.test(value) && !value.includes('//')

function decodeItem(raw: unknown, view: ApprovalFollowUpView): ApprovalFollowUpItem | null {
  if (!object(raw) || !exact(raw, FOLLOW_UP_ITEM_FIELDS) || !kind(raw.kind) || !guid(raw.itemId) || !text(raw.documentLabel, 200)
    || !appLink(raw.link) || !instant(raw.waitingSinceUtc) || raw.step !== view || !permission(raw.approverPermission)
    || !(raw.escalationPermission === null || permission(raw.escalationPermission)) || !text(raw.ruleVersion, 256)
    || !(raw.dueAtUtc === null || instant(raw.dueAtUtc)) || !instant(raw.recordedAtUtc)
    || typeof raw.recipientCount !== 'number' || !Number.isInteger(raw.recipientCount) || raw.recipientCount < 0) return null
  // An escalated item always names the permission it was escalated to and a computed due time.
  if (view === 'escalated' && (raw.escalationPermission === null || raw.dueAtUtc === null || raw.recipientCount < 1)) return null
  return raw as unknown as ApprovalFollowUpItem
}

export function decodeApprovalFollowUpPage(raw: unknown, companyId: string, view: ApprovalFollowUpView): ApprovalFollowUpPage | null {
  if (!object(raw) || !exact(raw, FOLLOW_UP_PAGE_FIELDS) || raw.companyId !== companyId || raw.view !== view
    || typeof raw.hasMore !== 'boolean' || !Array.isArray(raw.items) || raw.items.length > 100) return null
  const items: ApprovalFollowUpItem[] = []
  for (const entry of raw.items) {
    const item = decodeItem(entry, view)
    if (item === null) return null
    items.push(item)
  }
  const keys = new Set(items.map((item) => `${item.kind}|${item.itemId}|${item.waitingSinceUtc}`))
  return keys.size === items.length ? { companyId, view, items, hasMore: raw.hasMore } : null
}

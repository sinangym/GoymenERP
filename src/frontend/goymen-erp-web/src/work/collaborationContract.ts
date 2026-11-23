/**
 * W1-C (AI-D20/AI-D23/AI-D29) — strict decoders for the Work collaboration surface: record notes and mentions, tasks,
 * the personal two-class inbox and the people picker. Every answer is `unknown` until it decodes with EXACTLY the
 * OpenAPI keys (contracts/openapi/goymen-erp-v1.json); anything else is `null` (fail closed, "Beklenmeyen yanıt").
 * The answer's company must be the active company; a page's record must be the requested record.
 */

// WP9 appends seven record kinds (the server's order; existing names never move). A dispatch note's notes are its loading
// instruction's; a goods receipt's notes stay on its purchase order (the owner reads a receipt only by order + acceptance).
export const WORK_RECORD_TYPES = ['sales-order-draft', 'purchase-order', 'stock-label', 'manufacturing-plan', 'counterparty',
  'sales-invoice', 'loading-instruction', 'purchase-incoming-invoice', 'manufacturing-order', 'finance-manual-receipt',
  'finance-manual-bank-payment', 'sales-quote'] as const
export type WorkRecordType = (typeof WORK_RECORD_TYPES)[number]

export const MY_TASKS_ROUTE = '/gorevlerim'
export const workRecordNotesPath = (type: WorkRecordType, id: string) => `/work/records/${type}/${encodeURIComponent(id)}/notes`
export const workRecordTasksPath = (type: WorkRecordType, id: string) => `/work/records/${type}/${encodeURIComponent(id)}/tasks`
export const workNotePassivationPath = (noteId: string) => `/work/notes/${encodeURIComponent(noteId)}/passivation`
export const workNoteReadPath = (noteId: string) => `/work/notes/${encodeURIComponent(noteId)}/read`
export const WORK_TASKS_PATH = '/work/tasks'
export const WORK_MY_TASKS_PATH = '/work/tasks/mine'
export const workTaskCompletionPath = (taskId: string) => `/work/tasks/${encodeURIComponent(taskId)}/completion`
export const workTaskCancellationPath = (taskId: string) => `/work/tasks/${encodeURIComponent(taskId)}/cancellation`
export const WORK_INBOX_PATH = '/work/inbox-notifications'
export const workInboxReadPath = (id: string) => `/work/inbox-notifications/${encodeURIComponent(id)}/read`
export const WORK_PEOPLE_PATH = '/work/people'

export const WORK_LIMITS = { noteBody: 4000, mentions: 20, taskTitle: 200, taskDetails: 2000, reason: 500, page: 100, peopleQuery: 100, noteReads: 100 } as const

export interface WorkNote {
  readonly id: string; readonly companyId: string; readonly recordType: WorkRecordType; readonly recordId: string
  readonly authorUserId: string; readonly authorDisplayName: string; readonly body: string; readonly correctsNoteId: string | null
  readonly mentionedUserIds: readonly string[]; readonly createdAtUtc: string; readonly passivatedAtUtc: string | null
  readonly passivationReason: string | null
  /** WP9: when the CALLER marked the note read (null: not yet); `reads` = who read it and when, oldest first. */
  readonly readByMeAtUtc: string | null; readonly reads: readonly WorkNoteRead[]
}
export interface WorkNoteRead { readonly userId: string; readonly displayName: string; readonly readAtUtc: string }
export interface WorkNoteReadReceipt { readonly noteId: string; readonly readAtUtc: string }
export interface WorkNotePage { readonly companyId: string; readonly recordType: WorkRecordType; readonly recordId: string; readonly items: readonly WorkNote[]; readonly hasMore: boolean }

export type WorkTaskStatus = 'open' | 'completed' | 'cancelled'
export interface WorkTask {
  readonly id: string; readonly companyId: string; readonly recordType: WorkRecordType | null; readonly recordId: string | null
  readonly title: string; readonly details: string | null; readonly assignerUserId: string; readonly assignerDisplayName: string
  readonly assigneeUserId: string; readonly assigneeDisplayName: string; readonly dueAtUtc: string; readonly status: WorkTaskStatus
  readonly version: string; readonly createdAtUtc: string; readonly closedAtUtc: string | null; readonly closedByUserId: string | null
  readonly cancelReason: string | null
}
export interface WorkTaskPage { readonly companyId: string; readonly items: readonly WorkTask[]; readonly hasMore: boolean }

export type WorkInboxKind = 'mention' | 'task-assigned' | 'task-overdue'
  // D9 (WP2): event notifications moved into the single store ("özet").
  | 'allocation-released' | 'purchase-supply-revived'
  // WP2 (AI-D29, YOL-D17, YOL-D28): approval follow-up ("şimdi").
  | 'approval-waiting' | 'approval-reminder' | 'approval-escalated' | 'approval-orphaned'
export const WORK_INBOX_KINDS: readonly WorkInboxKind[] = ['mention', 'task-assigned', 'task-overdue', 'allocation-released',
  'purchase-supply-revived', 'approval-waiting', 'approval-reminder', 'approval-escalated', 'approval-orphaned']
const DIGEST_KINDS: readonly WorkInboxKind[] = ['task-assigned', 'allocation-released', 'purchase-supply-revived']
const APPROVAL_KINDS: readonly WorkInboxKind[] = ['approval-waiting', 'approval-reminder', 'approval-escalated', 'approval-orphaned']
export type WorkApprovalFollowUpStep = 'waiting' | 'reminder' | 'escalated' | 'orphaned'
/** WP2: the approval cycle an approval notification is about; the decision is given on `link` (the owner's screen). */
export interface WorkInboxApprovalRef {
  readonly kind: string; readonly itemId: string; readonly documentLabel: string; readonly link: string
  readonly waitingSinceUtc: string; readonly step: WorkApprovalFollowUpStep; readonly dueAtUtc: string | null
}
/** D9 (WP2): the owner event an event notification carries. */
export interface WorkInboxEventRef {
  readonly sourceEntityId: string; readonly sourceVersion: string; readonly reasonCode: string; readonly reasonText: string
  readonly occurredAtUtc: string; readonly salesOrderId: string | null; readonly salesOrderLineId: string | null
  readonly purchaseOrderId: string | null; readonly purchaseOrderLineId: string | null
}
export type WorkInboxClass = 'now' | 'digest'
export interface WorkInboxItem {
  readonly id: string; readonly companyId: string; readonly kind: WorkInboxKind; readonly class: WorkInboxClass; readonly sourceId: string
  readonly recordType: WorkRecordType | null; readonly recordId: string | null; readonly actorUserId: string | null
  readonly recordedAtUtc: string; readonly visibleAtUtc: string; readonly readAtUtc: string | null
  readonly approval: WorkInboxApprovalRef | null; readonly event: WorkInboxEventRef | null
}
export interface WorkInboxPage { readonly companyId: string; readonly items: readonly WorkInboxItem[]; readonly hasMore: boolean; readonly unreadNowCount: number; readonly unreadDigestCount: number }
export interface WorkInboxRead { readonly id: string; readonly readAtUtc: string }
/** WP9 / D7: the picker answers only id + display name (active members of the company only). */
export interface WorkPerson { readonly userId: string; readonly displayName: string }
export interface WorkPeoplePage { readonly companyId: string; readonly people: readonly WorkPerson[]; readonly hasMore: boolean }

export const NOTE_FIELDS = ['id', 'companyId', 'recordType', 'recordId', 'authorUserId', 'authorDisplayName', 'body', 'correctsNoteId', 'mentionedUserIds', 'createdAtUtc', 'passivatedAtUtc', 'passivationReason', 'readByMeAtUtc', 'reads'] as const
export const NOTE_READ_FIELDS = ['userId', 'displayName', 'readAtUtc'] as const
export const NOTE_PAGE_FIELDS = ['companyId', 'recordType', 'recordId', 'items', 'hasMore'] as const
export const TASK_FIELDS = ['id', 'companyId', 'recordType', 'recordId', 'title', 'details', 'assignerUserId', 'assignerDisplayName', 'assigneeUserId', 'assigneeDisplayName', 'dueAtUtc', 'status', 'version', 'createdAtUtc', 'closedAtUtc', 'closedByUserId', 'cancelReason'] as const
export const TASK_PAGE_FIELDS = ['companyId', 'items', 'hasMore'] as const
export const INBOX_FIELDS = ['id', 'companyId', 'kind', 'class', 'sourceId', 'recordType', 'recordId', 'actorUserId', 'recordedAtUtc', 'visibleAtUtc', 'readAtUtc', 'approval', 'event'] as const
export const INBOX_APPROVAL_FIELDS = ['kind', 'itemId', 'documentLabel', 'link', 'waitingSinceUtc', 'step', 'dueAtUtc'] as const
export const INBOX_EVENT_FIELDS = ['sourceEntityId', 'sourceVersion', 'reasonCode', 'reasonText', 'occurredAtUtc', 'salesOrderId', 'salesOrderLineId', 'purchaseOrderId', 'purchaseOrderLineId'] as const
export const INBOX_PAGE_FIELDS = ['companyId', 'items', 'hasMore', 'unreadNowCount', 'unreadDigestCount'] as const
export const PERSON_FIELDS = ['userId', 'displayName'] as const
export const PEOPLE_PAGE_FIELDS = ['companyId', 'people', 'hasMore'] as const

const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, names: readonly string[]) => Object.keys(value).length === names.length && names.every((name) => Object.hasOwn(value, name))
export const guid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const instant = (value: unknown): value is string => typeof value === 'string' && value.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(value) && Number.isFinite(Date.parse(value))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= max
const nullable = <T>(value: unknown, check: (v: unknown) => v is T): boolean => value === null || check(value)
const recordType = (value: unknown): value is WorkRecordType => typeof value === 'string' && (WORK_RECORD_TYPES as readonly string[]).includes(value)
const version = (value: unknown): value is string => typeof value === 'string' && /^[1-9]\d{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n
const count = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0
const distinctIds = (items: readonly { readonly id: string }[]) => new Set(items.map((item) => item.id)).size === items.length

function decodeNote(raw: unknown, companyId: string): WorkNote | null {
  if (!object(raw) || !exact(raw, NOTE_FIELDS) || !guid(raw.id) || raw.companyId !== companyId || !recordType(raw.recordType) || !guid(raw.recordId)
    || !guid(raw.authorUserId) || !text(raw.authorDisplayName, 400) || !text(raw.body, WORK_LIMITS.noteBody) || !nullable(raw.correctsNoteId, guid)
    || !Array.isArray(raw.mentionedUserIds) || raw.mentionedUserIds.length > WORK_LIMITS.mentions || !raw.mentionedUserIds.every(guid)
    || new Set(raw.mentionedUserIds).size !== raw.mentionedUserIds.length || !instant(raw.createdAtUtc)
    || !nullable(raw.passivatedAtUtc, instant) || (raw.passivatedAtUtc === null) !== (raw.passivationReason === null)
    || (raw.passivationReason !== null && !text(raw.passivationReason, WORK_LIMITS.reason)) || raw.correctsNoteId === raw.id
    || !nullable(raw.readByMeAtUtc, instant) || !Array.isArray(raw.reads) || raw.reads.length > WORK_LIMITS.noteReads) return null
  const readers = new Set<string>()
  for (const read of raw.reads as unknown[]) {
    // The author never has a read receipt of their own note; a reader appears once.
    if (!object(read) || !exact(read, NOTE_READ_FIELDS) || !guid(read.userId) || !text(read.displayName, 400) || !instant(read.readAtUtc)
      || read.userId === raw.authorUserId || readers.has(read.userId)) return null
    readers.add(read.userId)
  }
  return raw as unknown as WorkNote
}

export function decodeWorkNoteReadReceipt(raw: unknown, noteId: string): WorkNoteReadReceipt | null {
  if (!object(raw) || !exact(raw, ['noteId', 'readAtUtc']) || raw.noteId !== noteId || !instant(raw.readAtUtc)) return null
  return { noteId, readAtUtc: raw.readAtUtc }
}

export function decodeWorkNote(raw: unknown, companyId: string, recordId?: string): WorkNote | null {
  const note = decodeNote(raw, companyId)
  return note === null || (recordId !== undefined && note.recordId !== recordId) ? null : note
}

export function decodeWorkNotePage(raw: unknown, companyId: string, type: WorkRecordType, recordId: string): WorkNotePage | null {
  if (!object(raw) || !exact(raw, NOTE_PAGE_FIELDS) || raw.companyId !== companyId || raw.recordType !== type || raw.recordId !== recordId
    || typeof raw.hasMore !== 'boolean' || !Array.isArray(raw.items) || raw.items.length > WORK_LIMITS.page) return null
  const items: WorkNote[] = []
  for (const item of raw.items) {
    const note = decodeNote(item, companyId)
    if (note === null || note.recordType !== type || note.recordId !== recordId) return null
    items.push(note)
  }
  return distinctIds(items) ? { companyId, recordType: type, recordId, items, hasMore: raw.hasMore } : null
}

function decodeTask(raw: unknown, companyId: string): WorkTask | null {
  if (!object(raw) || !exact(raw, TASK_FIELDS) || !guid(raw.id) || raw.companyId !== companyId || !nullable(raw.recordType, recordType)
    || !nullable(raw.recordId, guid) || (raw.recordType === null) !== (raw.recordId === null) || !text(raw.title, WORK_LIMITS.taskTitle)
    || !nullable(raw.details, (v): v is string => text(v, WORK_LIMITS.taskDetails)) || !guid(raw.assignerUserId) || !text(raw.assignerDisplayName, 400)
    || !guid(raw.assigneeUserId) || !text(raw.assigneeDisplayName, 400) || !instant(raw.dueAtUtc) || !version(raw.version) || !instant(raw.createdAtUtc)) return null
  const open = raw.status === 'open'
  if (open) {
    if (raw.version !== '1' || raw.closedAtUtc !== null || raw.closedByUserId !== null || raw.cancelReason !== null) return null
  } else if (raw.status === 'completed') {
    if (raw.version !== '2' || !instant(raw.closedAtUtc) || raw.closedByUserId !== raw.assigneeUserId || raw.cancelReason !== null) return null
  } else if (raw.status === 'cancelled') {
    if (raw.version !== '2' || !instant(raw.closedAtUtc) || raw.closedByUserId !== raw.assignerUserId || !text(raw.cancelReason, WORK_LIMITS.reason)) return null
  } else return null
  return raw as unknown as WorkTask
}

export function decodeWorkTask(raw: unknown, companyId: string, taskId?: string): WorkTask | null {
  const task = decodeTask(raw, companyId)
  return task === null || (taskId !== undefined && task.id !== taskId) ? null : task
}

export function decodeWorkTaskPage(raw: unknown, companyId: string): WorkTaskPage | null {
  if (!object(raw) || !exact(raw, TASK_PAGE_FIELDS) || raw.companyId !== companyId || typeof raw.hasMore !== 'boolean'
    || !Array.isArray(raw.items) || raw.items.length > WORK_LIMITS.page) return null
  const items: WorkTask[] = []
  for (const item of raw.items) {
    const task = decodeTask(item, companyId)
    if (task === null) return null
    items.push(task)
  }
  return distinctIds(items) ? { companyId, items, hasMore: raw.hasMore } : null
}

const approvalKind = (value: unknown): value is string => typeof value === 'string' && value.length <= 160 && /^[a-z][a-z0-9-]*(?:\.[a-z0-9-]+)+$/.test(value)
const appLink = (value: unknown): value is string => typeof value === 'string' && value.length <= 512 && /^\/[A-Za-z0-9/_\-?=&.%]*$/.test(value) && !value.includes('//')
const followUpStep = (value: unknown): value is WorkApprovalFollowUpStep => value === 'waiting' || value === 'reminder' || value === 'escalated' || value === 'orphaned'
const stepOf: Readonly<Record<string, WorkApprovalFollowUpStep>> = {
  'approval-waiting': 'waiting', 'approval-reminder': 'reminder', 'approval-escalated': 'escalated', 'approval-orphaned': 'orphaned',
}

function decodeApprovalRef(raw: unknown): WorkInboxApprovalRef | null {
  if (!object(raw) || !exact(raw, INBOX_APPROVAL_FIELDS) || !approvalKind(raw.kind) || !guid(raw.itemId) || !text(raw.documentLabel, 200)
    || !appLink(raw.link) || !instant(raw.waitingSinceUtc) || !followUpStep(raw.step) || !nullable(raw.dueAtUtc, instant)) return null
  return raw as unknown as WorkInboxApprovalRef
}

function decodeEventRef(raw: unknown, kind: WorkInboxKind): WorkInboxEventRef | null {
  if (!object(raw) || !exact(raw, INBOX_EVENT_FIELDS) || !guid(raw.sourceEntityId) || !text(raw.sourceVersion, 128) || !text(raw.reasonCode, 64)
    || !text(raw.reasonText, 4000) || !instant(raw.occurredAtUtc) || !nullable(raw.salesOrderId, guid) || !nullable(raw.salesOrderLineId, guid)
    || !nullable(raw.purchaseOrderId, guid) || !nullable(raw.purchaseOrderLineId, guid)) return null
  const sales = raw.salesOrderId !== null && raw.salesOrderLineId !== null && raw.purchaseOrderId === null && raw.purchaseOrderLineId === null
  const purchase = kind === 'purchase-supply-revived' && raw.purchaseOrderId !== null && raw.purchaseOrderLineId !== null
    && raw.salesOrderId === null && raw.salesOrderLineId === null
  return sales || purchase ? raw as unknown as WorkInboxEventRef : null
}

function decodeInboxItem(raw: unknown, companyId: string): WorkInboxItem | null {
  if (!object(raw) || !exact(raw, INBOX_FIELDS) || !guid(raw.id) || raw.companyId !== companyId
    || !(WORK_INBOX_KINDS as readonly unknown[]).includes(raw.kind)
    || (raw.class !== 'now' && raw.class !== 'digest') || !guid(raw.sourceId) || !nullable(raw.recordType, recordType) || !nullable(raw.recordId, guid)
    || (raw.recordType === null) !== (raw.recordId === null) || !nullable(raw.actorUserId, guid) || !instant(raw.recordedAtUtc)
    || !instant(raw.visibleAtUtc) || !nullable(raw.readAtUtc, instant)) return null
  const kind = raw.kind as WorkInboxKind
  // AI-D29 classes as the server assigns them; a mismatch is a contract break, never re-classified here.
  const expectedClass = DIGEST_KINDS.includes(kind) ? 'digest' : 'now'
  if (raw.class !== expectedClass || (kind === 'mention' && (raw.recordId === null || raw.actorUserId === null))
    || (kind === 'task-overdue' && raw.actorUserId !== null) || (kind === 'task-assigned' && raw.actorUserId === null)) return null
  const isApproval = APPROVAL_KINDS.includes(kind)
  const isEvent = kind === 'allocation-released' || kind === 'purchase-supply-revived'
  if ((isApproval || isEvent) && (raw.actorUserId !== null || raw.recordType !== null)) return null
  // An approval notification may come without its follow-up row only if the server could not join it; the step must match.
  const approval = raw.approval === null ? null : decodeApprovalRef(raw.approval)
  if ((raw.approval !== null && approval === null) || (!isApproval && raw.approval !== null)
    || (approval !== null && approval.step !== stepOf[kind])) return null
  const event = raw.event === null ? null : decodeEventRef(raw.event, kind)
  if ((raw.event !== null && event === null) || isEvent !== (raw.event !== null)) return null
  return { ...(raw as unknown as WorkInboxItem), approval, event }
}

export function decodeWorkInboxPage(raw: unknown, companyId: string): WorkInboxPage | null {
  if (!object(raw) || !exact(raw, INBOX_PAGE_FIELDS) || raw.companyId !== companyId || typeof raw.hasMore !== 'boolean'
    || !count(raw.unreadNowCount) || !count(raw.unreadDigestCount) || !Array.isArray(raw.items) || raw.items.length > WORK_LIMITS.page) return null
  const items: WorkInboxItem[] = []
  for (const item of raw.items) {
    const decoded = decodeInboxItem(item, companyId)
    if (decoded === null) return null
    items.push(decoded)
  }
  return distinctIds(items) ? { companyId, items, hasMore: raw.hasMore, unreadNowCount: raw.unreadNowCount, unreadDigestCount: raw.unreadDigestCount } : null
}

export function decodeWorkInboxRead(raw: unknown, id: string): WorkInboxRead | null {
  if (!object(raw) || !exact(raw, ['id', 'readAtUtc']) || raw.id !== id || !instant(raw.readAtUtc)) return null
  return { id, readAtUtc: raw.readAtUtc }
}

export function decodeWorkPeoplePage(raw: unknown, companyId: string): WorkPeoplePage | null {
  if (!object(raw) || !exact(raw, PEOPLE_PAGE_FIELDS) || raw.companyId !== companyId || typeof raw.hasMore !== 'boolean' || !Array.isArray(raw.people) || raw.people.length > 50) return null
  const people: WorkPerson[] = []
  for (const person of raw.people) {
    if (!object(person) || !exact(person, PERSON_FIELDS) || !guid(person.userId) || !text(person.displayName, 400)
      || people.some((previous) => previous.userId === person.userId)) return null
    people.push(person as unknown as WorkPerson)
  }
  return { companyId, people, hasMore: raw.hasMore }
}

/** A refusal is read by its stable code only, never by localized text. */
export function problemCode(raw: unknown): string | null {
  return object(raw) && typeof raw.code === 'string' && /^[a-z0-9_.]{1,64}$/.test(raw.code) ? raw.code : null
}

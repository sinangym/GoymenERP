import { describe, expect, it } from 'vitest'
import {
  NOTE_FIELDS, WORK_RECORD_TYPES, decodeWorkInboxPage, decodeWorkNotePage, decodeWorkNoteReadReceipt, decodeWorkPeoplePage, decodeWorkTask,
  decodeWorkTaskPage, problemCode,
} from './collaborationContract'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER = 'bbbbbbbb-0000-4000-8000-000000000002'
const RECORD = 'cccccccc-0000-4000-8000-000000000001'
const AUTHOR = 'dddddddd-0000-4000-8000-000000000001'
const READER = 'eeeeeeee-0000-4000-8000-000000000001'

export const note = (overrides: Record<string, unknown> = {}) => ({
  id: 'ffffffff-0000-4000-8000-000000000001', companyId: COMPANY, recordType: 'sales-order-draft', recordId: RECORD,
  authorUserId: AUTHOR, authorDisplayName: 'SENTETIK Yazar', body: 'SENTETIK not', correctsNoteId: null, mentionedUserIds: [READER],
  createdAtUtc: '2026-11-22T09:00:00+00:00', passivatedAtUtc: null, passivationReason: null, readByMeAtUtc: null, reads: [], ...overrides,
})
export const task = (overrides: Record<string, unknown> = {}) => ({
  id: '12345678-0000-4000-8000-000000000001', companyId: COMPANY, recordType: null, recordId: null, title: 'SENTETIK görev', details: null,
  assignerUserId: AUTHOR, assignerDisplayName: 'SENTETIK Atayan', assigneeUserId: READER, assigneeDisplayName: 'SENTETIK Atanan',
  dueAtUtc: '2026-11-23T09:00:00+00:00', status: 'open', version: '1', createdAtUtc: '2026-11-22T09:00:00+00:00',
  closedAtUtc: null, closedByUserId: null, cancelReason: null, ...overrides,
})
export const inboxItem = (overrides: Record<string, unknown> = {}) => ({
  id: '87654321-0000-4000-8000-000000000001', companyId: COMPANY, kind: 'mention', class: 'now', sourceId: 'ffffffff-0000-4000-8000-000000000001',
  recordType: 'sales-order-draft', recordId: RECORD, actorUserId: AUTHOR, recordedAtUtc: '2026-11-22T09:00:00+00:00',
  visibleAtUtc: '2026-11-22T09:00:00+00:00', readAtUtc: null, approval: null, event: null, ...overrides,
})
const notePage = (items: unknown[]) => ({ companyId: COMPANY, recordType: 'sales-order-draft', recordId: RECORD, items, hasMore: false })

describe('W1-C collaboration decoders', () => {
  it('accepts an exact note page for the requested record only', () => {
    expect(decodeWorkNotePage(notePage([note()]), COMPANY, 'sales-order-draft', RECORD)?.items).toHaveLength(1)
    expect(decodeWorkNotePage(notePage([note()]), OTHER, 'sales-order-draft', RECORD)).toBeNull()
    expect(decodeWorkNotePage(notePage([note()]), COMPANY, 'purchase-order', RECORD)).toBeNull()
    expect(decodeWorkNotePage(notePage([note({ recordId: OTHER })]), COMPANY, 'sales-order-draft', RECORD)).toBeNull()
  })

  it('refuses missing, extra, mistyped and inconsistent note fields', () => {
    const missing: Record<string, unknown> = note()
    delete missing[NOTE_FIELDS[0]]
    for (const bad of [missing, note({ extra: 1 }), note({ body: '' }), note({ mentionedUserIds: [READER, READER] }),
      note({ passivatedAtUtc: '2026-11-22T10:00:00Z', passivationReason: null }), note({ recordType: 'invoice' }), note({ createdAtUtc: 'dün' })]) {
      expect(decodeWorkNotePage(notePage([bad]), COMPANY, 'sales-order-draft', RECORD)).toBeNull()
    }
  })

  it('accepts a task only in a consistent state and version', () => {
    expect(decodeWorkTaskPage({ companyId: COMPANY, items: [task()], hasMore: false }, COMPANY)?.items).toHaveLength(1)
    expect(decodeWorkTask(task({ status: 'completed', version: '2', closedAtUtc: '2026-11-22T10:00:00Z', closedByUserId: READER }), COMPANY)).not.toBeNull()
    expect(decodeWorkTask(task({ status: 'completed', version: '2', closedAtUtc: '2026-11-22T10:00:00Z', closedByUserId: AUTHOR }), COMPANY)).toBeNull()
    expect(decodeWorkTask(task({ status: 'cancelled', version: '2', closedAtUtc: '2026-11-22T10:00:00Z', closedByUserId: AUTHOR, cancelReason: null }), COMPANY)).toBeNull()
    expect(decodeWorkTask(task({ status: 'open', version: '2' }), COMPANY)).toBeNull()
    expect(decodeWorkTask(task({ recordType: 'purchase-order', recordId: null }), COMPANY)).toBeNull()
    expect(decodeWorkTask(task(), COMPANY, OTHER)).toBeNull()
  })

  it('accepts the server class of each inbox kind and refuses a re-classified one', () => {
    const page = (items: unknown[]) => ({ companyId: COMPANY, items, hasMore: false, unreadNowCount: 1, unreadDigestCount: 0 })
    expect(decodeWorkInboxPage(page([inboxItem()]), COMPANY)?.unreadNowCount).toBe(1)
    expect(decodeWorkInboxPage(page([inboxItem({ kind: 'task-assigned', class: 'digest', recordType: null, recordId: null })]), COMPANY)).not.toBeNull()
    expect(decodeWorkInboxPage(page([inboxItem({ kind: 'task-overdue', actorUserId: null })]), COMPANY)).not.toBeNull()
    expect(decodeWorkInboxPage(page([inboxItem({ class: 'digest' })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([inboxItem({ kind: 'task-assigned', class: 'now' })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage({ ...page([inboxItem()]), unreadNowCount: -1 }, COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([inboxItem({ companyId: OTHER })]), COMPANY)).toBeNull()
  })

  it('WP2: decodes approval follow-up notifications with their step and the moved event notifications as digest', () => {
    const page = (items: unknown[]) => ({ companyId: COMPANY, items, hasMore: false, unreadNowCount: 1, unreadDigestCount: 0 })
    const approval = { kind: 'logistics.dispatch-note.v1', itemId: RECORD, documentLabel: 'SENTETIK irsaliye',
      link: '/lojistik/yukleme-talimatlari/' + RECORD, waitingSinceUtc: '2026-11-23T06:00:00+00:00', step: 'reminder', dueAtUtc: '2026-11-23T10:00:00+00:00' }
    const approvalItem = (overrides: Record<string, unknown> = {}) => inboxItem({ kind: 'approval-reminder', class: 'now', recordType: null,
      recordId: null, actorUserId: null, approval, ...overrides })
    expect(decodeWorkInboxPage(page([approvalItem()]), COMPANY)?.items[0]?.approval?.step).toBe('reminder')
    // The step must match the kind, the class is "şimdi", and an approval kind carries no actor or record type.
    expect(decodeWorkInboxPage(page([approvalItem({ kind: 'approval-escalated' })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([approvalItem({ class: 'digest' })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([approvalItem({ approval: { ...approval, link: 'https://example.invalid/x' } })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([approvalItem({ approval: { ...approval, extra: 1 } })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([inboxItem({ approval })]), COMPANY)).toBeNull()
    const event = { sourceEntityId: OTHER, sourceVersion: '2', reasonCode: 'acceptance-link-failed', reasonText: 'SENTETIK ayrım bırakıldı',
      occurredAtUtc: '2026-11-17T00:00:00+00:00', salesOrderId: RECORD, salesOrderLineId: READER, purchaseOrderId: null, purchaseOrderLineId: null }
    const eventItem = (overrides: Record<string, unknown> = {}) => inboxItem({ kind: 'allocation-released', class: 'digest', recordType: null,
      recordId: null, actorUserId: null, event, ...overrides })
    expect(decodeWorkInboxPage(page([eventItem()]), COMPANY)?.items[0]?.event?.reasonCode).toBe('acceptance-link-failed')
    expect(decodeWorkInboxPage(page([eventItem({ class: 'now' })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([eventItem({ event: null })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([eventItem({ event: { ...event, purchaseOrderId: OTHER, purchaseOrderLineId: OTHER } })]), COMPANY)).toBeNull()
    expect(decodeWorkInboxPage(page([inboxItem({ kind: 'approval-unknown' })]), COMPANY)).toBeNull()
  })

  it('D7: accepts people as id + display name only and reads a refusal by its code', () => {
    const person = { userId: READER, displayName: 'SENTETIK Okur' }
    expect(decodeWorkPeoplePage({ companyId: COMPANY, people: [person], hasMore: false }, COMPANY)?.people).toEqual([person])
    // Anything beyond the two keys (the old membership flag, an e-mail) is a contract break.
    expect(decodeWorkPeoplePage({ companyId: COMPANY, people: [{ ...person, isActiveInCompany: true }], hasMore: false }, COMPANY)).toBeNull()
    expect(decodeWorkPeoplePage({ companyId: COMPANY, people: [{ ...person, email: 'x@example.invalid' }], hasMore: false }, COMPANY)).toBeNull()
    expect(decodeWorkPeoplePage({ companyId: COMPANY, people: [person, person], hasMore: false }, COMPANY)).toBeNull()
    expect(problemCode({ code: 'work_mention_refused' })).toBe('work_mention_refused')
    expect(problemCode({ code: 'Bir şey' })).toBeNull()
  })

  it('WP9: decodes the read receipts of a note and refuses an author receipt, a duplicate reader or extra keys', () => {
    const read = { userId: READER, displayName: 'SENTETIK Okur', readAtUtc: '2026-11-22T10:00:00Z' }
    const page = decodeWorkNotePage(notePage([note({ reads: [read], readByMeAtUtc: '2026-11-22T10:00:00Z' })]), COMPANY, 'sales-order-draft', RECORD)
    expect(page?.items[0].reads).toEqual([read])
    expect(page?.items[0].readByMeAtUtc).toBe('2026-11-22T10:00:00Z')
    for (const bad of [note({ reads: [{ ...read, userId: AUTHOR }] }), note({ reads: [read, read] }), note({ reads: [{ ...read, extra: 1 }] }),
      note({ reads: null }), note({ readByMeAtUtc: 'dün' })]) {
      expect(decodeWorkNotePage(notePage([bad]), COMPANY, 'sales-order-draft', RECORD)).toBeNull()
    }
    expect(decodeWorkNoteReadReceipt({ noteId: RECORD, readAtUtc: '2026-11-22T10:00:00Z' }, RECORD)).toEqual({ noteId: RECORD, readAtUtc: '2026-11-22T10:00:00Z' })
    expect(decodeWorkNoteReadReceipt({ noteId: OTHER, readAtUtc: '2026-11-22T10:00:00Z' }, RECORD)).toBeNull()
  })

  it('WP9: knows the twelve server record kinds in the server order and nothing else', () => {
    expect(WORK_RECORD_TYPES).toEqual(['sales-order-draft', 'purchase-order', 'stock-label', 'manufacturing-plan', 'counterparty',
      'sales-invoice', 'loading-instruction', 'purchase-incoming-invoice', 'manufacturing-order', 'finance-manual-receipt',
      'finance-manual-bank-payment', 'sales-quote'])
    expect(decodeWorkNotePage({ ...notePage([note({ recordType: 'sales-quote' })]), recordType: 'sales-quote' }, COMPANY, 'sales-quote', RECORD)).not.toBeNull()
    expect(decodeWorkNotePage(notePage([note({ recordType: 'goods-receipt' })]), COMPANY, 'sales-order-draft', RECORD)).toBeNull()
  })
})

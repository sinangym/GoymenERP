import { describe, expect, it } from 'vitest'
import {
  NOTE_FIELDS, decodeWorkInboxPage, decodeWorkNotePage, decodeWorkPeoplePage, decodeWorkTask, decodeWorkTaskPage, problemCode,
} from './collaborationContract'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER = 'bbbbbbbb-0000-4000-8000-000000000002'
const RECORD = 'cccccccc-0000-4000-8000-000000000001'
const AUTHOR = 'dddddddd-0000-4000-8000-000000000001'
const READER = 'eeeeeeee-0000-4000-8000-000000000001'

export const note = (overrides: Record<string, unknown> = {}) => ({
  id: 'ffffffff-0000-4000-8000-000000000001', companyId: COMPANY, recordType: 'sales-order-draft', recordId: RECORD,
  authorUserId: AUTHOR, authorDisplayName: 'SENTETIK Yazar', body: 'SENTETIK not', correctsNoteId: null, mentionedUserIds: [READER],
  createdAtUtc: '2026-11-22T09:00:00+00:00', passivatedAtUtc: null, passivationReason: null, ...overrides,
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
  visibleAtUtc: '2026-11-22T09:00:00+00:00', readAtUtc: null, ...overrides,
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

  it('accepts only active people of the company and reads a refusal by its code', () => {
    const person = { userId: READER, displayName: 'SENTETIK Okur', isActiveInCompany: true }
    expect(decodeWorkPeoplePage({ companyId: COMPANY, people: [person], hasMore: false }, COMPANY)?.people).toHaveLength(1)
    expect(decodeWorkPeoplePage({ companyId: COMPANY, people: [{ ...person, isActiveInCompany: false }], hasMore: false }, COMPANY)).toBeNull()
    expect(decodeWorkPeoplePage({ companyId: COMPANY, people: [person, person], hasMore: false }, COMPANY)).toBeNull()
    expect(problemCode({ code: 'work_mention_refused' })).toBe('work_mention_refused')
    expect(problemCode({ code: 'Bir şey' })).toBeNull()
  })
})

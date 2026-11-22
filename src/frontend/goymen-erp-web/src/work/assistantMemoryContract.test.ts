import { describe, expect, it } from 'vitest'
import { decodeAssistantMemoryDeletion, decodeAssistantMemoryEntry, decodeAssistantMemoryStatus } from './assistantMemoryContract'

/** WP-C6 (AI-D30). SENTETIK data only. */
const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OWNER = 'bbbbbbbb-0000-4000-8000-0000000000c1'
const ENTRY = 'cccccccc-0000-4000-8000-0000000000c6'
const entry = (overrides: Record<string, unknown> = {}) => ({
  id: ENTRY, companyId: COMPANY, ownerUserId: OWNER, kind: 'note', body: 'SENTETIK', state: 'active', version: '1',
  createdAtUtc: '2026-11-22T09:00:00+00:00', updatedAtUtc: '2026-11-22T09:00:00+00:00', expiresAtUtc: '2026-12-22T09:00:00+00:00',
  closedAtUtc: null, closeReason: null, ...overrides,
})

describe('WP-C6 assistant memory decoders', () => {
  it('accepts exactly the server keys for the actor and active company', () => {
    expect(decodeAssistantMemoryEntry(entry(), COMPANY, OWNER)).not.toBeNull()
    expect(decodeAssistantMemoryEntry(entry({ state: 'closed', closedAtUtc: '2026-11-23T09:00:00+00:00' }), COMPANY, OWNER)).not.toBeNull()
    expect(decodeAssistantMemoryEntry(entry({ state: 'passive' }), COMPANY, OWNER)).not.toBeNull()
  })

  it('fails closed on an extra key, another owner, another company, a bad kind or an inconsistent closure', () => {
    expect(decodeAssistantMemoryEntry({ ...entry(), extra: 1 }, COMPANY, OWNER)).toBeNull()
    expect(decodeAssistantMemoryEntry(entry({ ownerUserId: ENTRY }), COMPANY, OWNER)).toBeNull()
    expect(decodeAssistantMemoryEntry(entry({ companyId: OWNER }), COMPANY, OWNER)).toBeNull()
    expect(decodeAssistantMemoryEntry(entry({ kind: 'instruction' }), COMPANY, OWNER)).toBeNull()
    expect(decodeAssistantMemoryEntry(entry({ state: 'closed' }), COMPANY, OWNER)).toBeNull()
    // AI-D39: an overseer's closure reason never reaches the owner; an answer that carries one is refused.
    expect(decodeAssistantMemoryEntry(entry({ state: 'closed', closedAtUtc: '2026-11-23T09:00:00+00:00', closeReason: 'x' }), COMPANY, OWNER)).toBeNull()
    expect(decodeAssistantMemoryEntry(entry({ state: 'expired' }), COMPANY, OWNER)).toBeNull()
    expect(decodeAssistantMemoryEntry(entry(), COMPANY, OWNER, OWNER)).toBeNull()
  })

  it('requires writable to agree with the reasons and the deletion to name the deleted entry only', () => {
    const status = {
      companyId: COMPANY, userId: OWNER, writable: false, disabledReasons: ['pilot_off'], pilotEnabled: false, retentionDays: null,
      maxEntriesPerUser: null, informationNoticeVersion: null, activeEntryCount: 0,
    }
    expect(decodeAssistantMemoryStatus(status, COMPANY, OWNER)).not.toBeNull()
    expect(decodeAssistantMemoryStatus({ ...status, writable: true }, COMPANY, OWNER)).toBeNull()
    expect(decodeAssistantMemoryStatus({ ...status, disabledReasons: ['unknown'] }, COMPANY, OWNER)).toBeNull()
    expect(decodeAssistantMemoryDeletion({ id: ENTRY, version: '2', deletedAtUtc: '2026-11-22T10:00:00+00:00' }, ENTRY)).not.toBeNull()
    expect(decodeAssistantMemoryDeletion({ id: ENTRY, version: '2', deletedAtUtc: '2026-11-22T10:00:00+00:00', body: 'x' }, ENTRY)).toBeNull()
  })
})

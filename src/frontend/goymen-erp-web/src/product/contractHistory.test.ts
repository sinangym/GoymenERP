import { describe, expect, it } from 'vitest'
import { HISTORY_PAGE_SIZE, decodeProductHistory } from './contract'
import { PRODUCT_1, historyEntry, productHistory } from './testHarness'

/* `ProductHistoryView` decoder (ProductHistoryContractsV1.cs). */

const edit = (over: Record<string, unknown> = {}) =>
  historyEntry({
    eventId: 'eeeeeeee-0000-0000-0000-000000000002',
    operation: 'DraftBasicEdit',
    occurredAtUtc: '2026-09-11T09:30:00Z',
    versionBefore: '1',
    versionAfter: '2',
    reason: 'Gerekçe',
    changedFields: ['name'],
    ...over,
  })

const decode = (over: Record<string, unknown>) => decodeProductHistory(productHistory(over), PRODUCT_1)

describe('decodeProductHistory', () => {
  it('accepts a create and an edit, newest first, verbatim', () => {
    const history = decode({ entries: [edit(), historyEntry()] })
    expect(history).not.toBeNull()
    expect(history?.entries.map((entry) => entry.versionAfter)).toEqual(['2', '1'])
    expect(history?.entries[0].changedFields).toEqual(['name'])
    expect(history?.truncated).toBe(false)
  })

  it('keeps versions past 2^53 as exact strings and checks the step as integers', () => {
    const history = decode({
      entries: [edit({ versionBefore: '9007199254740992', versionAfter: '9007199254740993' })],
    })
    expect(history?.entries[0].versionAfter).toBe('9007199254740993')
    // As doubles these two are equal; as integers the step is 0 and must be refused.
    expect(decode({ entries: [edit({ versionBefore: '9007199254740993', versionAfter: '9007199254740993' })] })).toBeNull()
  })

  it.each([
    ['another product', { productId: '99999999-9999-9999-9999-999999999999' }],
    ['no entries', { entries: [] }],
    ['missing truncated flag', { truncated: undefined }],
    ['numeric version', { entries: [edit({ versionAfter: 2 })] }],
    ['edit skipping a version', { entries: [edit({ versionAfter: '3' })] }],
    ['edit without a before version', { entries: [edit({ versionBefore: null })] }],
    ['create with a before version', { entries: [historyEntry({ versionBefore: '1', versionAfter: '2' })] }],
    ['create not at version 1', { entries: [historyEntry({ versionAfter: '2' })] }],
    ['create claiming changed fields', { entries: [historyEntry({ changedFields: ['name'] })] }],
    ['blank reason', { entries: [edit({ reason: ' ' })] }],
    ['zone-less time', { entries: [edit({ occurredAtUtc: '2026-09-11T09:30:00' })] }],
    ['nil event id', { entries: [edit({ eventId: '00000000-0000-0000-0000-000000000000' })] }],
    ['duplicate event id', { entries: [edit({ eventId: historyEntry().eventId }), historyEntry()] }],
    ['duplicate changed field', { entries: [edit({ changedFields: ['name', 'name'] })] }],
    ['oldest first', { entries: [historyEntry(), edit()] }],
  ])('refuses %s', (_label, over) => {
    expect(decode(over)).toBeNull()
  })

  it('refuses more entries than the server page size', () => {
    const entries = Array.from({ length: HISTORY_PAGE_SIZE + 1 }, (_, index) =>
      edit({
        eventId: `eeeeeeee-0000-0000-0000-${String(index + 10).padStart(12, '0')}`,
        occurredAtUtc: new Date(Date.UTC(2026, 8, 11, 9, 0, 0) - index * 60_000).toISOString(),
        versionBefore: String(200 - index),
        versionAfter: String(201 - index),
      }),
    )
    expect(decode({ entries })).toBeNull()
    expect(decode({ entries: entries.slice(0, HISTORY_PAGE_SIZE), truncated: true })?.entries).toHaveLength(HISTORY_PAGE_SIZE)
  })

  it('keeps an unknown operation and field key visible instead of rejecting the history', () => {
    const history = decode({ entries: [edit({ operation: 'Activate', changedFields: ['lifecycle'] })] })
    expect(history?.entries[0].operation).toEqual({ known: false, raw: 'Activate' })
    expect(history?.entries[0].changedFields).toEqual(['lifecycle'])
  })
})

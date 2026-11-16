/** API-98/B-14 decoder and input cases: the strict view decoder and the client-side code and name rules. */

import { describe, expect, it } from 'vitest'
import {
  REFERENCE_LIST,
  decodeReferenceListView,
  isReferenceListCode,
  normalizeReferenceListName,
  referenceListEntryVersionsPath,
  referenceListPath,
} from './referenceListContract'

const ENTRY = {
  id: 'cccccccc-0000-4000-8000-00000000000c',
  code: 'K-01',
  name: 'SENTETIK',
  active: true,
  version: '1',
  recordedBy: 'sentetik-kullanici',
  recordedAtUtc: '2026-11-15T09:00:00Z',
}

describe('reference list contract', () => {
  it('opens exactly the four lists and no operation type route', () => {
    expect(Object.keys(REFERENCE_LIST).sort()).toEqual(['expenseType', 'paintColor', 'scrapKind', 'serviceType'])
    expect(Object.values(REFERENCE_LIST).map((list) => list.kind)).not.toContain('OperationType')
    expect(referenceListPath('serviceType')).toBe('/settings/reference-lists/service-types')
    expect(referenceListEntryVersionsPath('scrapKind', ENTRY.id)).toBe(
      `/settings/reference-lists/scrap-kinds/entries/${ENTRY.id}/versions`,
    )
  })

  it('accepts a code exactly as the server does', () => {
    for (const code of ['A', '9', 'K-01', 'K_01.B', 'A'.repeat(32)]) expect(isReferenceListCode(code)).toBe(true)
    for (const code of ['', ' A', 'a', '-A', 'A B', 'İ', 'A'.repeat(33), 1]) expect(isReferenceListCode(code)).toBe(false)
  })

  it('normalizes a name by trimming and refuses blank, control and over-long names', () => {
    expect(normalizeReferenceListName('  Gümrük ')).toBe('Gümrük')
    expect(normalizeReferenceListName('   ')).toBeNull()
    expect(normalizeReferenceListName('A\tB')).toBeNull()
    expect(normalizeReferenceListName('a'.repeat(201))).toBeNull()
  })

  it('decodes a well-formed view and an empty one', () => {
    expect(decodeReferenceListView('expenseType', { kind: 'ExpenseType', entries: [ENTRY] })?.entries).toHaveLength(1)
    expect(decodeReferenceListView('paintColor', { kind: 'PaintColor', entries: [] })?.entries).toEqual([])
  })

  it('fails closed on another kind, a repeated code or id, a zero id and extra keys', () => {
    expect(decodeReferenceListView('expenseType', { kind: 'ServiceType', entries: [] })).toBeNull()
    expect(
      decodeReferenceListView('expenseType', {
        kind: 'ExpenseType',
        entries: [ENTRY, { ...ENTRY, id: 'cccccccc-0000-4000-8000-00000000000d' }],
      }),
    ).toBeNull()
    expect(decodeReferenceListView('expenseType', { kind: 'ExpenseType', entries: [ENTRY, { ...ENTRY, code: 'K-02' }] })).toBeNull()
    expect(
      decodeReferenceListView('expenseType', {
        kind: 'ExpenseType',
        entries: [{ ...ENTRY, id: '00000000-0000-0000-0000-000000000000' }],
      }),
    ).toBeNull()
    expect(decodeReferenceListView('expenseType', { kind: 'ExpenseType', entries: [], extra: 1 })).toBeNull()
  })
})

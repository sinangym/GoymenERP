import { describe, expect, it } from 'vitest'
import { activeGroupOf, type DrawnGroup } from './activeGroup'

/**
 * The first-segment fallback of the active-group rule, on synthetic groups. No live first segment is shared by two
 * groups after KLN-D1 (every `/lojistik/…` entry is in Stok & Lojistik, and `/stok` is settled by `NAV_PATH_OWNERS`),
 * so the "two owners claim nothing" branch would otherwise go untested.
 */
const leaf = (key: string, path: string) => ({ key, path, label: key }) as unknown as DrawnGroup['leaves'][number]

describe('activeGroupOf first-segment fallback', () => {
  it('claims no work area when two drawn groups own the same first segment', () => {
    const groups: DrawnGroup[] = [
      { id: 'purchasing', label: 'A', leaves: [leaf('a', '/ortak/a')] },
      { id: 'stock-logistics', label: 'B', leaves: [leaf('b', '/ortak/b')] },
    ]
    expect(activeGroupOf(groups, '/ortak/bilinmeyen')).toBeNull()
  })

  it('opens the single owner of a first segment for a page under no entry', () => {
    const groups: DrawnGroup[] = [
      { id: 'purchasing', label: 'A', leaves: [leaf('a', '/alis/a')] },
      { id: 'stock-logistics', label: 'B', leaves: [leaf('b', '/ortak/b')] },
    ]
    expect(activeGroupOf(groups, '/ortak/bilinmeyen')).toBe('stock-logistics')
  })

  it('still prefers the longest entry path over the segment fallback', () => {
    const groups: DrawnGroup[] = [
      { id: 'purchasing', label: 'A', leaves: [leaf('a', '/ortak/a')] },
      { id: 'stock-logistics', label: 'B', leaves: [leaf('b', '/ortak/b')] },
    ]
    expect(activeGroupOf(groups, '/ortak/b/123')).toBe('stock-logistics')
  })
})

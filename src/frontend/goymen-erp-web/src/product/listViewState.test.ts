import { afterEach, describe, expect, it } from 'vitest'
import {
  DEFAULT_PRODUCT_LIST_VIEW,
  clearedFilters,
  isFiltered,
  readProductListView,
  rememberOpenedRecord,
  resetProductListView,
  takeOpenedRecord,
  writeProductListView,
} from './listViewState'

const NS_A = '["actor-a","single","company-a","gen-1","3"]'
const NS_B = '["actor-b","single","company-b","gen-2","4"]'

afterEach(() => {
  resetProductListView()
})

describe('default view', () => {
  it('shows every lifecycle, so a just-created Draft is not hidden', () => {
    /**
     * ASSUMPTION-PRD-001. §6.1 makes every new Product a Draft, and the operator who
     * just created one arrives here to find it. An `Active` default would hide their
     * own new record behind a filter they did not set.
     */
    expect(DEFAULT_PRODUCT_LIST_VIEW.lifecycle).toBe('all')
    expect(isFiltered(DEFAULT_PRODUCT_LIST_VIEW)).toBe(false)
  })
})

describe('isFiltered', () => {
  it('counts search, lifecycle, family, roles and completeness', () => {
    expect(isFiltered({ ...DEFAULT_PRODUCT_LIST_VIEW, q: '  ' })).toBe(false)
    expect(isFiltered({ ...DEFAULT_PRODUCT_LIST_VIEW, q: 'sac' })).toBe(true)
    expect(isFiltered({ ...DEFAULT_PRODUCT_LIST_VIEW, lifecycle: 'Draft' })).toBe(true)
    expect(isFiltered({ ...DEFAULT_PRODUCT_LIST_VIEW, family: 'Boru' })).toBe(true)
    expect(isFiltered({ ...DEFAULT_PRODUCT_LIST_VIEW, roles: ['sellable'] })).toBe(true)
    expect(isFiltered({ ...DEFAULT_PRODUCT_LIST_VIEW, completeness: 'incomplete' })).toBe(true)
  })

  it('does not count sort — it changes order, never which records match', () => {
    expect(isFiltered({ ...DEFAULT_PRODUCT_LIST_VIEW, sort: 'name', direction: 'desc' })).toBe(false)
  })
})

describe('clearedFilters', () => {
  it('clears the filters and keeps the chosen sort and page size', () => {
    const cleared = clearedFilters({
      ...DEFAULT_PRODUCT_LIST_VIEW,
      q: 'sac',
      lifecycle: 'Inactive',
      roles: ['purchasable'],
      sort: 'lastChangedAt',
      direction: 'desc',
      pageSize: 100,
    })
    expect(cleared.q).toBe('')
    expect(cleared.lifecycle).toBe('all')
    expect(cleared.roles).toEqual([])
    // Neither is a filter; resetting them would undo a choice the user did not ask
    // to undo.
    expect(cleared.sort).toBe('lastChangedAt')
    expect(cleared.direction).toBe('desc')
    expect(cleared.pageSize).toBe(100)
  })
})

describe('namespace isolation', () => {
  it('never returns another namespace’s view', () => {
    writeProductListView(NS_A, { ...DEFAULT_PRODUCT_LIST_VIEW, q: 'gizli parça' })
    expect(readProductListView(NS_B)).toEqual(DEFAULT_PRODUCT_LIST_VIEW)
    expect(readProductListView(NS_A).q).toBe('gizli parça')
  })

  it('is a no-op while the shell holds no bound context', () => {
    writeProductListView(NS_A, { ...DEFAULT_PRODUCT_LIST_VIEW, q: 'sac' })
    // Mid-switch: writing under null would clobber the view the user is about to
    // return to.
    writeProductListView(null, { ...DEFAULT_PRODUCT_LIST_VIEW, q: 'boru' })
    expect(readProductListView(NS_A).q).toBe('sac')
    expect(readProductListView(null)).toEqual(DEFAULT_PRODUCT_LIST_VIEW)
  })

  it('keeps only the view currently in use', () => {
    writeProductListView(NS_A, { ...DEFAULT_PRODUCT_LIST_VIEW, q: 'sac' })
    writeProductListView(NS_B, { ...DEFAULT_PRODUCT_LIST_VIEW, q: 'boru' })
    expect(readProductListView(NS_A)).toEqual(DEFAULT_PRODUCT_LIST_VIEW)
  })
})

describe('opened record marker', () => {
  it('is read once and then cleared', () => {
    rememberOpenedRecord(NS_A, 'p-1')
    expect(takeOpenedRecord(NS_A)).toBe('p-1')
    // Returning to the list is a one-time event; a marker left behind would move
    // focus again on an unrelated later visit.
    expect(takeOpenedRecord(NS_A)).toBeNull()
  })

  it('is unreachable from another namespace', () => {
    rememberOpenedRecord(NS_A, 'p-1')
    expect(takeOpenedRecord(NS_B)).toBeNull()
  })
})

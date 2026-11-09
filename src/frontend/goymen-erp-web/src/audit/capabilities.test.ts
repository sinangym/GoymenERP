import { describe, expect, it } from 'vitest'
import {
  AUDIT_CAPABILITY,
  AUDIT_FILTERS,
  AUDIT_ROUTE,
  EMPTY_FILTERS,
  auditListPath,
  filtersFromSearch,
  hasActiveFilter,
  overLongFilters,
} from './capabilities'
import { auditEventsPath } from './api'

describe('audit capability and filters', () => {
  it('mirrors the Identity 1.1 declaration and the server filter limits', () => {
    expect(AUDIT_CAPABILITY.list).toBe('identity.audit.list')
    expect(AUDIT_ROUTE[AUDIT_CAPABILITY.list]).toBe('/ayarlar/denetim-kaydi')
    expect(AUDIT_FILTERS.map((filter) => [filter.key, filter.maxLength])).toEqual([
      ['correlationId', 128],
      ['entityType', 160],
      ['entityId', 128],
    ])
  })

  it('keeps only the non-empty trimmed filters in the URL and reads them back trimmed', () => {
    expect(auditListPath()).toBe('/ayarlar/denetim-kaydi')
    const path = auditListPath({ correlationId: ' SENTETIK-corr ', entityType: '', entityId: 'SO 1' })
    expect(path).toBe('/ayarlar/denetim-kaydi?korelasyon=SENTETIK-corr&kayitNo=SO+1')
    const search = new URLSearchParams(path.split('?')[1])
    expect(filtersFromSearch(search)).toEqual({ correlationId: 'SENTETIK-corr', entityType: '', entityId: 'SO 1' })
    expect(filtersFromSearch(new URLSearchParams('kayitTuru=%20%20'))).toEqual(EMPTY_FILTERS)
    expect(hasActiveFilter(EMPTY_FILTERS)).toBe(false)
  })

  it('flags exactly the filters over the server limit, measured after trimming', () => {
    expect(overLongFilters({ correlationId: 'x'.repeat(128), entityType: 'y'.repeat(160), entityId: 'z'.repeat(128) })).toEqual([])
    expect(overLongFilters({ correlationId: 'x'.repeat(129), entityType: 'y'.repeat(161), entityId: 'z'.repeat(129) })).toEqual([
      'correlationId',
      'entityType',
      'entityId',
    ])
  })

  it('sends the page size, the opaque cursor and only the present filters', () => {
    expect(auditEventsPath(EMPTY_FILTERS, null)).toBe('/audit/events?pageSize=25')
    expect(auditEventsPath({ correlationId: 'c', entityType: 'T', entityId: 'i' }, 'abc+/=')).toBe(
      '/audit/events?pageSize=25&cursor=abc%2B%2F%3D&correlationId=c&entityType=T&entityId=i',
    )
  })
})

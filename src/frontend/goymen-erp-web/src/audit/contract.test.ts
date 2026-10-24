/** The audit page decoder fails closed and keeps only the minimal fields (./contract.ts). */

import { describe, expect, it } from 'vitest'
import { decodeAuditEventPage } from './contract'
import { COMPANY_A, COMPANY_B, auditItem, auditPage } from './testHarness'

describe('decodeAuditEventPage', () => {
  it('decodes a page and keeps only the minimal fields, versions as decimal strings', () => {
    const page = decodeAuditEventPage(auditPage([auditItem({ entityVersionBefore: null, entityVersionAfter: 1 })], 'c1'), COMPANY_A)
    expect(page).not.toBeNull()
    expect(page?.nextCursor).toBe('c1')
    const row = page!.items[0]
    expect(row.entityVersionBefore).toBeNull()
    expect(row.entityVersionAfter).toBe('1')
    expect(Object.keys(row).sort()).toEqual(
      [
        'id', 'occurredAtUtc', 'eventType', 'entityType', 'entityId', 'operation', 'entityVersionBefore',
        'entityVersionAfter', 'actorType', 'actorId', 'permissionCode', 'sourceChannel', 'aiInvolvement', 'outcome',
        'correlationId',
      ].sort(),
    )
    const serialized = JSON.stringify(page)
    for (const secret of ['GİZLİ', '203.0.113.7', 'sha256:abc', 'ORG_ADMIN']) expect(serialized).not.toContain(secret)
  })

  it('accepts an empty page with no cursor (no data, or a scoped grant the server answers empty)', () => {
    expect(decodeAuditEventPage(auditPage([]), COMPANY_A)).toEqual({ items: [], pageSize: 25, nextCursor: null })
  })

  it('rejects a row from another company — a fence breach is never filtered out quietly', () => {
    expect(decodeAuditEventPage(auditPage([auditItem(), auditItem({ companyId: COMPANY_B })]), COMPANY_A)).toBeNull()
  })

  it.each([
    ['an extra key on a row', () => auditPage([{ ...auditItem(), extra: 1 }])],
    ['a missing key on a row', () => auditPage([(({ userAgent: _drop, ...rest }) => rest)(auditItem())])],
    ['an extra key on the page', () => ({ ...auditPage([]), total: 0 })],
    ['a version beyond the exact integer range', () => auditPage([auditItem({ entityVersionAfter: 2 ** 53 })])],
    ['a fractional version', () => auditPage([auditItem({ entityVersionAfter: 1.5 })])],
    ['a version as a string', () => auditPage([auditItem({ entityVersionAfter: '4' })])],
    ['an instant without an offset', () => auditPage([auditItem({ occurredAtUtc: '2026-10-24T09:15:30' })])],
    ['an empty correlation id', () => auditPage([auditItem({ correlationId: ' ' })])],
    ['a non-string reason', () => auditPage([auditItem({ reason: null })])],
    ['a role code that is not a string', () => auditPage([auditItem({ roleCodes: [1] })])],
    ['a duplicate id', () => { const one = auditItem(); return auditPage([one, { ...one }]) }],
    ['more rows than the page size', () => auditPage([auditItem(), auditItem()], null, 1)],
    ['a page size above the server bound', () => auditPage([], null, 101)],
    ['a cursor on an empty page', () => auditPage([], 'c1')],
    ['an over-long cursor', () => auditPage([auditItem()], 'x'.repeat(129))],
    ['items that are not an array', () => ({ items: {}, pageSize: 25, nextCursor: null })],
  ])('rejects %s', (_name, build) => {
    expect(decodeAuditEventPage(build(), COMPANY_A)).toBeNull()
  })
})

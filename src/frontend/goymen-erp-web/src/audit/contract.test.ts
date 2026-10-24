/** The audit page decoder fails closed and keeps only the minimal fields (./contract.ts). */

import { describe, expect, it } from 'vitest'
import { decodeAuditEventDetails, decodeAuditEventPage } from './contract'
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

  it('accepts a row without the detail fields (details moved to their own endpoint)', () => {
    const minimal = (({ roleCodes: _a, beforeJson: _b, afterJson: _c, reason: _d, clientIpAddress: _e, userAgent: _f, ...rest }) => rest)(auditItem())
    expect(decodeAuditEventPage(auditPage([minimal]), COMPANY_A)?.items).toHaveLength(1)
  })

  it('rejects a row carrying only some of the detail fields', () => {
    const partial = (({ beforeJson: _b, ...rest }) => rest)(auditItem())
    expect(decodeAuditEventPage(auditPage([partial]), COMPANY_A)).toBeNull()
  })
})

describe('decodeAuditEventDetails', () => {
  const ID = '00000001-aaaa-4aaa-8aaa-000000000001'
  const details = (overrides: Record<string, unknown> = {}) => ({
    id: ID, beforeJson: '{"a":1}', afterJson: '{"a":2}', reason: 'Düzeltme', clientIpAddress: null, userAgent: 'UA', roleCodes: ['ADMIN'],
    ...overrides,
  })

  it('decodes the details of the requested event', () => {
    expect(decodeAuditEventDetails(details(), ID)).toEqual(details())
  })

  it.each([
    ['another event', details({ id: '00000002-aaaa-4aaa-8aaa-000000000002' })],
    ['an extra key', { ...details(), integrityHash: 'x' }],
    ['a null payload', details({ beforeJson: null })],
    ['an empty role code', details({ roleCodes: [''] })],
  ])('rejects %s', (_name, raw) => {
    expect(decodeAuditEventDetails(raw, ID)).toBeNull()
  })
})

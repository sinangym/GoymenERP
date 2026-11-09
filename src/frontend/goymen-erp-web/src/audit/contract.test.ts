import { describe, expect, it } from 'vitest'
import { AUDIT_ITEM_FIELDS, decodeAuditEventPage } from './contract'
import { COMPANY_A, COMPANY_B, auditEvent, page } from './testHarness'

describe('audit event page decoder (OpenAPI AuditEventPage / AuditEventItem)', () => {
  it('decodes an exact page and drops client IP and user agent from the decoded item', () => {
    const raw = auditEvent({ approvalContextJson: '{"approvedBy":"SENTETIK"}', causationId: 'SENTETIK-cause' })
    const decoded = decodeAuditEventPage(page([raw], 'SENTETIK-cursor', 25), COMPANY_A)

    expect(decoded).not.toBeNull()
    expect(decoded?.pageSize).toBe(25)
    expect(decoded?.nextCursor).toBe('SENTETIK-cursor')
    expect(decoded?.items).toHaveLength(1)
    const item = decoded!.items[0]
    expect(item.id).toBe(raw.id)
    expect(item.approvalContextJson).toBe('{"approvedBy":"SENTETIK"}')
    expect('clientIpAddress' in item).toBe(false)
    expect('userAgent' in item).toBe(false)
    expect(JSON.stringify(item)).not.toContain('203.0.113.77')
  })

  it('accepts an empty last page, null versions and the company id in another letter case', () => {
    expect(decodeAuditEventPage(page([]), COMPANY_A)).toEqual({ items: [], pageSize: 25, nextCursor: null })
    const item = auditEvent({ entityVersionBefore: null, entityVersionAfter: null, companyId: COMPANY_A.toUpperCase() })
    expect(decodeAuditEventPage(page([item]), COMPANY_A)?.items[0].entityVersionBefore).toBeNull()
  })

  it('fails closed on a missing or extra page key and on a bad page size or cursor', () => {
    expect(decodeAuditEventPage({ items: [], pageSize: 25 }, COMPANY_A)).toBeNull()
    expect(decodeAuditEventPage({ ...page([]), total: 0 }, COMPANY_A)).toBeNull()
    expect(decodeAuditEventPage(page([], null, 0), COMPANY_A)).toBeNull()
    expect(decodeAuditEventPage(page([], null, 101), COMPANY_A)).toBeNull()
    expect(decodeAuditEventPage(page([], null, 2.5), COMPANY_A)).toBeNull()
    expect(decodeAuditEventPage(page([], ''), COMPANY_A)).toBeNull()
    expect(decodeAuditEventPage(page([auditEvent(), auditEvent()], null, 1), COMPANY_A)).toBeNull()
    expect(decodeAuditEventPage([], COMPANY_A)).toBeNull()
    expect(decodeAuditEventPage(null, COMPANY_A)).toBeNull()
  })

  it.each(AUDIT_ITEM_FIELDS.map((field) => [field]))('fails closed when item key %s is missing', (field) => {
    const raw = auditEvent()
    delete raw[field]
    expect(decodeAuditEventPage(page([raw]), COMPANY_A)).toBeNull()
  })

  it('fails closed on an extra item key, a wrong type or a non-nullable null', () => {
    const cases: Record<string, unknown>[] = [
      { extra: 'x' },
      { id: 'not-a-guid' },
      { id: '00000000-0000-0000-0000-000000000000' },
      { occurredAtUtc: '2026-11-01 09:30' },
      { occurredAtUtc: '2026-11-01T09:30:00' },
      { eventType: null },
      { entityVersionBefore: '3' },
      { entityVersionAfter: -1 },
      { entityVersionAfter: 1.5 },
      { roleCodes: 'SALES' },
      { roleCodes: [1] },
      { beforeJson: null },
      { reason: null },
      { approvalContextJson: 1 },
      { clientIpAddress: 1 },
      { userAgent: false },
      { causationId: 7 },
      { schemaVersion: '1' },
      { integrityHash: null },
    ]
    for (const override of cases) {
      expect(decodeAuditEventPage(page([auditEvent(override)]), COMPANY_A), JSON.stringify(override)).toBeNull()
    }
  })

  it('rejects the whole page when one item belongs to another company or an id repeats', () => {
    expect(decodeAuditEventPage(page([auditEvent(), auditEvent({ companyId: COMPANY_B })]), COMPANY_A)).toBeNull()
    const repeated = auditEvent()
    expect(decodeAuditEventPage(page([repeated, { ...repeated }]), COMPANY_A)).toBeNull()
  })
})

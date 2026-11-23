/** WP7-2 Work inbox kinds (Organizasyon / Cari / Ürün): listed once, named, with the owner's rule words. SENTETIK. */
import { describe, expect, it } from 'vitest'
import { WORK_INBOX_CAPABILITIES, WORK_INBOX_KINDS } from '../workInboxKinds'
import { inboxKindText, inboxModuleText, inboxPermissionText, inboxRoleText, inboxRuleText } from '../workInboxText'

const KINDS = [
  ['organization.company-tax-id-change.v1', 'organization'],
  ['counterparty.tax-identity-change.v1', 'counterparty'],
  ['product.package-standard-version.v1', 'product'],
  ['product.price-list-version.v1', 'product'],
] as const

const RULES = [
  'organization.company-tax-id-change-approval-required', 'organization.company-tax-id-change-stale', 'organization.company-tax-id-first',
  'counterparty.tax-identity-change-approval-required', 'counterparty.tax-identity-change-stale',
  'product.package-standard-approval-required', 'product.package-standard-entries', 'product.package-standard-customer-entries',
  'product.price-list-version-publication-required', 'product.price-list-next-version',
]

describe('WP7-2 master data inbox kinds', () => {
  it('lists every kind once under its module, with a Turkish name and module name', () => {
    for (const [kind, module] of KINDS) {
      const entries = WORK_INBOX_KINDS.filter((entry) => entry.kind === kind)
      expect(entries).toHaveLength(1)
      expect(entries[0].module).toBe(module)
      expect(inboxKindText(kind)).not.toBe(kind)
      expect(inboxModuleText(module)).not.toBe(module)
    }
  })

  it('has words for every rule code, reference role and decision permission the sources use', () => {
    for (const rule of RULES) expect(inboxRuleText(rule)).not.toBeNull()
    for (const role of ['party', 'product', 'price-list']) expect(inboxRoleText(role)).not.toBeNull()
    for (const permission of ['organization.company.tax-id.approve', 'counterparty.parties.tax-identity.approve',
      'product.package-standards.approve', 'product.price-lists.publish']) expect(inboxPermissionText(permission)).not.toBe(permission)
  })

  it('opens the inbox only for the price list publish control among these kinds', () => {
    expect(WORK_INBOX_CAPABILITIES).toContain('price-lists.publish')
    expect(WORK_INBOX_KINDS.filter((entry) => entry.kind !== 'product.price-list-version.v1'
      && KINDS.some(([kind]) => kind === entry.kind)).every((entry) => entry.capability === null)).toBe(true)
  })
})

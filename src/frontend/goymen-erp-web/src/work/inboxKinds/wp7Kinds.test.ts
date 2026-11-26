/** WP7 Work inbox kinds (Satın alma / Kimlik / Ayarlar): each listed, named, with the owner's rule words. SENTETIK. */
import { describe, expect, it } from 'vitest'
import { WORK_INBOX_CAPABILITIES, WORK_INBOX_KINDS } from '../workInboxKinds'
import { inboxKindText, inboxModuleText, inboxPermissionText, inboxRoleText, inboxRuleText } from '../workInboxText'

const KINDS = [
  ['purchasing.request.v1', 'purchasing'],
  ['purchasing.supplier-return-cost-exception.v1', 'purchasing'],
  ['purchasing.service-order.v1', 'purchasing'],
  ['identity.access-grant-request.v1', 'identity'],
  ['settings.finance-approval-policy.v1', 'settings'],
  ['settings.cash-count-difference-policy.v1', 'settings'],
  ['settings.bank-institution-policy.v1', 'settings'],
  ['settings.collateral-rate-policy.v1', 'settings'],
  ['settings.manufacturing-parameter-profile.v1', 'settings'],
  ['settings.manufacturing-tolerance-profile.v1', 'settings'],
] as const

// The owner rule codes the backend sources emit (PurchaseRequest/SupplierReturn/ServiceOrder, IdentityAccessGrantRequest,
// SettingsPolicy work inbox sources).
const RULES = [
  'purchasing.request-approval-required', 'purchasing.supplier-return-manual-cost', 'purchasing.supplier-return-quantity',
  'purchasing.service-order-approval-required', 'purchasing.service-order-price-revised',
  'purchasing.service-order-own-material-subcontract', 'identity.access-grant-approval-required',
  'identity.access-grant-second-approval', 'identity.access-grant-time-boxed', 'settings.policy-draft-awaiting-publication',
  'settings.profile-approved-awaiting-publication', 'settings.policy-next-version',
]

describe('WP7 inbox kinds', () => {
  it('lists every kind once under its module, with a Turkish name', () => {
    for (const [kind, module] of KINDS) {
      const entries = WORK_INBOX_KINDS.filter((entry) => entry.kind === kind)
      expect(entries).toHaveLength(1)
      expect(entries[0].module).toBe(module)
      expect(inboxKindText(kind)).not.toBe(kind)
      expect(inboxModuleText(module)).not.toBe(module)
    }
    expect(new Set(WORK_INBOX_KINDS.map((entry) => entry.kind)).size).toBe(WORK_INBOX_KINDS.length)
  })

  it('has words for every rule code, reference role and approve permission the sources use', () => {
    for (const rule of RULES) expect(inboxRuleText(rule)).not.toBeNull()
    for (const role of ['target-user', 'role', 'cash-account', 'work-center', 'company-account']) expect(inboxRoleText(role)).not.toBeNull()
    for (const permission of ['purchasing.requests.approve', 'identity.access.approve', 'settings.policies.publish',
      'settings.policies.publish-critical', 'settings.manufacturing-parameter-profiles.publish',
      'settings.manufacturing-tolerance-profiles.publish']) expect(inboxPermissionText(permission)).not.toBe(permission)
  })

  it('opens request decisions for their approve-only purpose route; other existing approve controls and Ayarlar publication has no capability yet', () => {
    expect(WORK_INBOX_CAPABILITIES).toContain('purchasing.request.work-decision')
    expect(WORK_INBOX_KINDS.find(entry => entry.kind === 'purchasing.request.v1')?.capability).toBe('purchasing.request.work-decision')
    expect(WORK_INBOX_CAPABILITIES).toContain('purchasing.service-orders.approve')
    expect(WORK_INBOX_CAPABILITIES).toContain('identity.access.approve')
    expect(WORK_INBOX_KINDS.filter((entry) => entry.module === 'settings').every((entry) => entry.capability === null)).toBe(true)
  })
})

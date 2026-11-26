import { describe, expect, it } from 'vitest'
import { WORK_INBOX_CAPABILITIES, WORK_INBOX_KINDS } from '../workInboxKinds'
import { inboxKindText, inboxPermissionText, inboxRoleText, inboxRuleText } from '../workInboxText'
import { WP6_INBOX_KIND_TEXT, WP6_INBOX_RULE_TEXT, WP6_INBOX_PERMISSION_TEXT } from './inventoryText'

// Same Node built-in access as the existing Logistics capability/source contract tests; no browser URL assumptions.
const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }

describe('WP6 iş kutusu türleri (Stok / Lojistik)', () => {
  it('transfer fazlası, transfer kaybı ve müşteri iadesi kendi modül listesinde ve Türkçe adıyla', () => {
    const kinds = WORK_INBOX_KINDS.map((entry) => entry.kind)
    const wp6Kinds = ['inventory.transfer-overage.v1', 'inventory.transfer-loss.v1', 'logistics.customer-return.v1']
    expect(Object.keys(WP6_INBOX_KIND_TEXT).sort()).toEqual([...wp6Kinds].sort())
    expect(kinds.filter(kind => wp6Kinds.includes(kind)).sort()).toEqual([...wp6Kinds].sort())
    expect(new Set(kinds).size).toBe(kinds.length)
    for (const kind of kinds) expect(inboxKindText(kind)).not.toBe(kind)
  })

  it('keeps the single actual opening-cost owner registration and its WP5 words, without the retired WP6 rule', () => {
    const registration = fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/InventoryOpeningCostRegistration.cs'), 'utf8')
    const source = fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/WorkInbox/InventoryOpeningCostWorkInboxSourceV1.cs'), 'utf8')
    expect(registration.match(/AddScoped<IWorkInboxSourceV1, WorkInbox\.InventoryOpeningCostWorkInboxSourceV1>/g)).toHaveLength(1)
    expect(source).toContain('KindName = "inventory.opening-cost.v1"')
    expect(source).toContain('ApprovalRequiredRule = "inventory.opening-cost-approval-required"')
    expect(WORK_INBOX_KINDS.filter(entry => entry.kind === 'inventory.opening-cost.v1')).toEqual([
      { kind: 'inventory.opening-cost.v1', module: 'inventory', capability: 'inventory.opening-cost.work-decision' },
    ])
    expect(inboxKindText('inventory.opening-cost.v1')).toBe('Stok açılış maliyeti onayı')
    expect(inboxPermissionText('inventory.opening-cost.approve')).toBe('Stok açılış maliyeti onayı')
    expect(inboxRuleText('inventory.opening-cost-approval-required')).toBe('Açılış birim maliyeti, girenden farklı bir kişinin kararını bekliyor')
    expect(inboxRuleText('inventory.opening-cost-submitted')).toBeNull()
  })

  it('mevcut transfer onay yetenekleri kutuyu açar; ekranı olmayan müşteri iadesi açmaz', () => {
    expect(WORK_INBOX_CAPABILITIES).toEqual(expect.arrayContaining([
      'inventory.stock-transfer.overage-approve', 'inventory.stock-transfer.loss-approve',
    ]))
    expect(WORK_INBOX_KINDS.find((entry) => entry.kind === 'logistics.customer-return.v1')?.capability).toBeNull()
  })

  it('kural, kayıt türü ve izin metinleri tanımlı', () => {
    const rules = [
      'inventory.transfer-overage-received', 'inventory.transfer-overage-renewal', 'inventory.transfer-overage-pieces',
      'inventory.transfer-loss-request', 'inventory.transfer-loss-renewed', 'logistics.customer-return-request',
    ]
    expect(Object.keys(WP6_INBOX_RULE_TEXT).sort()).toEqual([...rules].sort())
    for (const code of rules) expect(inboxRuleText(code)).not.toBeNull()
    expect(Object.keys(WP6_INBOX_PERMISSION_TEXT).sort()).toEqual([
      'inventory.stock-transfer.loss-approve', 'inventory.stock-transfer.overage-approve', 'logistics.customer-returns.decision',
    ])
    expect(inboxRoleText('inventory-transfer')).toBe('Transfer')
    expect(inboxPermissionText('logistics.customer-returns.decision')).toBe('Müşteri iadesi kabul kararı')
  })
})

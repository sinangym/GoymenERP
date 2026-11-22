import { describe, expect, it } from 'vitest'
import { WORK_INBOX_CAPABILITIES, WORK_INBOX_KINDS } from '../workInboxKinds'
import { inboxKindText, inboxPermissionText, inboxRoleText, inboxRuleText } from '../workInboxText'

describe('WP6 iş kutusu türleri (Stok / Lojistik)', () => {
  it('transfer fazlası, transfer kaybı ve müşteri iadesi kendi modül listesinde ve Türkçe adıyla', () => {
    const kinds = WORK_INBOX_KINDS.map((entry) => entry.kind)
    expect(kinds).toEqual(expect.arrayContaining([
      'inventory.transfer-overage.v1', 'inventory.transfer-loss.v1', 'logistics.customer-return.v1',
    ]))
    expect(new Set(kinds).size).toBe(kinds.length)
    for (const kind of kinds) expect(inboxKindText(kind)).not.toBe(kind)
  })

  it('mevcut transfer onay yetenekleri kutuyu açar; ekranı olmayan müşteri iadesi açmaz', () => {
    expect(WORK_INBOX_CAPABILITIES).toEqual(expect.arrayContaining([
      'inventory.stock-transfer.overage-approve', 'inventory.stock-transfer.loss-approve',
    ]))
    expect(WORK_INBOX_KINDS.find((entry) => entry.kind === 'logistics.customer-return.v1')?.capability).toBeNull()
  })

  it('kural, kayıt türü ve izin metinleri tanımlı', () => {
    for (const code of [
      'inventory.transfer-overage-received', 'inventory.transfer-overage-renewal', 'inventory.transfer-overage-pieces',
      'inventory.transfer-loss-request', 'inventory.transfer-loss-renewed', 'logistics.customer-return-request',
    ]) expect(inboxRuleText(code)).not.toBeNull()
    expect(inboxRoleText('inventory-transfer')).toBe('Transfer')
    expect(inboxPermissionText('logistics.customer-returns.decision')).toBe('Müşteri iadesi kabul kararı')
  })
})

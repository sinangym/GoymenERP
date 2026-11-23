/**
 * WP6 Work inbox words of the Inventory, Logistics and Manufacturing kinds added in this package, kept apart from
 * `workInboxText.ts` so parallel packages add their kinds without editing the same map entries.
 */
export const WP6_INBOX_KIND_TEXT: Readonly<Record<string, string>> = {
  'inventory.transfer-overage.v1': 'Transfer fazla teslim onayı',
  'inventory.transfer-loss.v1': 'Transfer yolda kayıp onayı',
  'logistics.customer-return.v1': 'Müşteri iadesi kabul kararı',
  'inventory.opening-cost.v1': 'Açılış maliyeti onayı',
}

export const WP6_INBOX_RULE_TEXT: Readonly<Record<string, string>> = {
  'inventory.transfer-overage-received': 'Kabulde sevk edilenden fazla kg geldi (sevk / kabul / fazla)',
  'inventory.transfer-overage-renewal': 'Reddedilen fazla için düzeltilmiş yeni talep (ilk bildirilen fazla / talep)',
  'inventory.transfer-overage-pieces': 'Adet takipli etiketin fazla adedi',
  'inventory.transfer-loss-request': 'Yolda kayıp talebi (sevkle yola çıkan kg)',
  'inventory.transfer-loss-renewed': 'Önceki kayıp talebi reddedildi veya geri çekildi; yeni talep',
  'inventory.opening-cost-submitted': 'Açılış stoğu maliyeti önerildi (şirket para birimi / kg)',
  'logistics.customer-return-request': 'Fiziksel müşteri iadesi talebi (teslim edilen / iade edilen miktar)',
}

export const WP6_INBOX_ROLE_TEXT: Readonly<Record<string, string>> = {
  'inventory-transfer': 'Transfer',
}

export const WP6_INBOX_PERMISSION_TEXT: Readonly<Record<string, string>> = {
  'inventory.stock-transfer.overage-approve': 'Transfer fazla teslim onayı',
  'inventory.stock-transfer.loss-approve': 'Transfer kayıp onayı',
  'logistics.customer-returns.decision': 'Müşteri iadesi kabul kararı',
  'inventory.opening-cost.approve': 'Açılış maliyeti onayı',
}

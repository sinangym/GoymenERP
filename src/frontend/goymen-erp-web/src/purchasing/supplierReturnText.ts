/**
 * B-19 (ALC-S5; SAT-D306, SAT-D392, SAT-D393, SAT-D396): Turkish texts of the supplier return section. The screen shows the
 * server's kg, cost basis and unit cost as received; it never computes a cost.
 */
export const SUPPLIER_RETURN_TEXT = {
  heading: 'Tedarikçiye iadeler',
  note: 'İade edilen etiket, asıl alış satırının birim maliyetiyle stoktan çıkar. Birim maliyet faturadan okunur; okunamazsa elle girilen maliyet istisna onayı bekler ve onaylanmadan stok çıkmaz.',
  show: 'Tedarikçiye iadeleri göster',
  hide: 'Tedarikçiye iadeleri gizle',
  subject: 'tedarikçiye iadeler',
  empty: 'Bu sipariş için tedarikçiye iade yok.',
  windowFull: 'Liste üst sınıra ulaştı; daha eski kayıtlar gösterilmiyor.',
  returnHeading: (index: number) => `İade ${index}`,
  status: 'Durum',
  version: 'Sürüm',
  sourceBlock: 'Kaynak blok',
  quantityKg: 'İade (kg)',
  costBasis: 'Maliyet dayanağı',
  unitCost: 'Birim maliyet (TRY/kg)',
  priceWithheld: 'Fiyat görme yetkiniz bulunmuyor.',
  reason: 'İade gerekçesi',
  occurredAtUtc: 'İade zamanı (UTC)',
  requestedBy: 'Giren',
  requestedAtUtc: 'Giriş zamanı (UTC)',
  decidedBy: 'Karar veren',
  decidedAtUtc: 'Karar zamanı (UTC)',
  decisionReason: 'Red gerekçesi',
  undoQuantity: 'Geri alınan kabul (alış biriminde)',
  expectedOrderVersion: 'Girişteki sipariş sürümü',
  replacementExpected: 'Yenisi bekleniyor',
  definitiveReturn: 'Kesin iade',
} as const

export const SUPPLIER_RETURN_STATUS_TEXT: Readonly<Record<string, string>> = {
  PendingCostException: 'Maliyet istisnası onayı bekliyor',
  Recorded: 'Stoktan çıktı',
  Rejected: 'Reddedildi',
}

export const SUPPLIER_RETURN_COST_BASIS_TEXT: Readonly<Record<string, string>> = {
  InvoiceUnitCost: 'Fatura birim maliyeti',
  ManualCostWithException: 'Elle girilen maliyet (istisna onayı)',
}

export const SUPPLIER_RETURN_SOURCE_BLOCK_TEXT: Readonly<Record<string, string>> = {
  Free: 'Serbest',
  Blocked: 'Bloke',
}

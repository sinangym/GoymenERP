/**
 * ALC-S13 / UI-K09 "Cari 360 sekmeleri" user texts (Turkish, single source). The tabs show only what each OWNER module
 * answers for this counterparty's account in the selected company: Satış (open-order quantities), Alış (purchase orders)
 * and Finans (the party-account balance summary and open items). Cari computes, sums and converts nothing.
 *
 * The Finance balance and open-item wording is Finance's own (`finance/partyLedgerText`, `finance/partyAccountsText`)
 * and is reused, never restated here, so the two screens cannot drift apart.
 */

export type Cari360Tab = 'sales' | 'purchasing' | 'finance'

export const CARI_360_TEXT = {
  heading: 'Cari 360',
  note:
    'Her sekme yalnız sahibi olan modülün bu cari hesap için verdiği yanıtı gösterir; bu kartta hesaplama, toplama ya da para birimi çevirme yapılmaz. Sekme açılmadan okuma yapılmaz.',
  tabsLabel: 'Cari 360 sekmeleri',
  tab: {
    sales: 'Satış',
    purchasing: 'Alış',
    finance: 'Finans',
  } satisfies Record<Cari360Tab, string>,
  pick: 'Görmek istediğiniz sekmeyi seçin.',
  sales: {
    subject: 'açık satış siparişleri',
    intro:
      'Satış modülünün açık sipariş raporu, yalnız bu cari hesap için: ürün ve birim başına müşterinin kabul ettiği miktar − sevk edilen. Farklı birimler toplanmaz.',
    caption: 'Bu carinin açık satış sipariş miktarları',
    product: 'Ürün',
    unit: 'Birim',
    orderCount: 'Sipariş sayısı',
    remaining: 'Açık miktar',
    awaitingAcceptance: 'Müşteri onayı bekliyor',
    none: '—',
    tooManyOrders: (count: number) =>
      `Bu grup ${count} siparişe yayılıyor; tek seferde ölçülemedi ve miktarı gösterilmiyor. Satış raporlarında tarih aralığını daraltın.`,
    incomplete: 'Eksik — bazı satırlar ölçülemedi; miktar bu satırları içermez.',
    empty: 'Satış modülü bu cari hesap için açık sipariş miktarı bildirmedi.',
    scanContinues:
      'İlk bölümde bu cari için açık miktar bulunamadı; tarama sunucuda sınırlı yapılıyor ve devamı var. Bu "açık sipariş yok" anlamına gelmez.',
    more: 'Bu sekme yalnız ilk bölümü gösterir; devamı Satış raporlarındadır.',
    openReport: 'Satış raporlarında bu cariyi aç',
    forbidden: 'Bu carinin satış raporunu görme yetkiniz yok.',
  },
  purchasing: {
    subject: 'satın alma siparişleri',
    intro:
      'Satın alma modülünün bu tedarikçi için kayıtlı siparişleri ve durumları. Fiyat veya stok gösterilmez.',
    caption: 'Bu carinin satın alma siparişleri',
    order: 'Sipariş',
    status: 'Durum',
    businessDate: 'İş tarihi',
    lineCount: 'Satır',
    empty: 'Satın alma modülü bu tedarikçi için sipariş bildirmedi.',
    more: 'Bu sekme yalnız ilk sayfayı gösterir; devamı Satın alma siparişlerindedir.',
    openList: 'Satın alma siparişlerinde bu tedarikçiyi aç',
    forbidden: 'Bu tedarikçinin satın alma siparişlerini görme yetkiniz yok.',
  },
  finance: {
    subject: 'cari hesap özeti',
    loading: 'Cari hesap özeti yükleniyor...',
    forbidden: 'Bu cari hesabın finans özetini görme yetkiniz yok.',
    unavailable: 'Finans cari hesap özeti şu anda alınamadı.',
    redacted: 'Bu cari hesabın tutarları bu yetkiyle gösterilmiyor; bakiye ve açık kalemler gizli.',
    withheld: 'Bu cari hesabın tutarları şu anda alınamadı; bakiye ve açık kalemler gösterilmiyor. Bu sıfır anlamına gelmez.',
  },
} as const

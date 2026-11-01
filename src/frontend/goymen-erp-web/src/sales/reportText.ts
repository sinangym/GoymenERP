/**
 * UI-80 "Satış raporları" user texts (Turkish, single source). Three read-only reports: açık sipariş miktarı (KPI No 124), kayıp
 * fırsatlar (SAT-D25) and fatura–sipariş fiyat sapması (No 25). The screen computes nothing; every number is the server's.
 */

import type { AmountState, UnmeasuredReason } from './reportContract'

export type ReportTab = 'open-orders' | 'lost' | 'price-variance'

export const REPORT_TABS: readonly ReportTab[] = ['open-orders', 'lost', 'price-variance']

export const AMOUNT_STATE_TEXT: Readonly<Record<AmountState, string>> = {
  Computed: 'Hesaplandı',
  Incomplete: 'Eksik — fiyatsız ya da fiyat birimi sipariş biriminden farklı satır var',
  AmountOutOfRange: 'Tutar hesaplanamadı (aralık dışı)',
}

export const UNMEASURED_REASON_TEXT: Readonly<Record<UnmeasuredReason, string>> = {
  PendingReapproval:
    'Ölçü alınacak miktar tam alınamadı: revizyon yeniden onay bekliyor ya da müşterinin kabul ettiği revizyonla karşılaştırılamıyor',
  TolerancePolicyUnavailable: 'Eksik sevk edildi ve teslim toleransı (Ayarlar) okunamadı; kalanın açık olup olmadığı bilinmiyor',
  DeliveryAnswerInvalid: 'Lojistik sevk bilgisi bu sipariş için okunamadı; miktar gösterilmiyor',
}

export const REPORT_TEXT = {
  heading: 'Satış raporları',
  intro: 'Salt okunur raporlar. Sayılar sunucudan gelir; bu ekran toplama, çevirme ya da yuvarlama yapmaz.',
  tabsLabel: 'Rapor türü',
  tab: {
    'open-orders': 'Açık siparişler',
    lost: 'Kayıp fırsatlar',
    'price-variance': 'Fiyat sapması',
  } satisfies Record<ReportTab, string>,
  filter: {
    legend: 'Süzgeç',
    from: 'Başlangıç tarihi',
    to: 'Bitiş tarihi',
    apply: 'Uygula',
    clear: 'Temizle',
    rangeInvalid: 'Bitiş tarihi başlangıç tarihinden önce olamaz.',
    onlyCustomer: 'Yalnız bu müşteri',
    allCustomers: 'Tüm müşteriler',
    filteredTo: 'Tek bir müşteriye daraltıldı.',
  },
  common: {
    customer: 'Müşteri',
    product: 'Ürün',
    currency: 'Para birimi',
    none: '—',
  },
  openOrders: {
    intro:
      'Kapatılmamış ve iptal edilmemiş siparişlerin açık miktarı: müşterinin kabul ettiği onaylı revizyonun miktarı − sevk edilen miktar (sıfırın altına inmez). Teslim toleransı içinde eksik sevkle tamamlanmış satırın kalanı açık sayılmaz. Müşteri onayı bekleyen miktar açık tonaja eklenmez, ayrı sütunda gösterilir. Tamamen sevk edilmiş ya da tamamlanmış satırlar listelenmez. Müşteri, ürün ve birim başına gruplanır; farklı birimler hiçbir zaman toplanmaz. Tarih süzgeci sipariş tarihine uygulanır.',
    subject: 'açık siparişler',
    caption: 'Açık sipariş miktarları',
    unit: 'Birim',
    orderCount: 'Sipariş sayısı',
    approved: 'Kabul edilen miktar',
    shipped: 'Sevk edilen',
    remaining: 'Açık miktar',
    awaitingAcceptance: 'Müşteri onayı bekliyor',
    awaitingRevision: (revision: string) => `${revision} müşteri onayı bekliyor`,
    noAcceptedRevision: 'Kabul edilmiş revizyon yok',
    completedWithinTolerance: 'Tolerans içinde tamamlandı',
    headerRevisionPending:
      'Cari veya para birimi revizyonu bekliyor; satır ölçülen (kabul edilen ya da onaylı) revizyonun carisinde gösterilir',
    detail: 'Sipariş satırları',
    detailCaption: (product: string, customer: string) => `${customer} — ${product} sipariş satırları`,
    order: 'Sipariş',
    orderDate: 'Sipariş tarihi',
    delivery: 'İstenen teslim',
    basisRevision: 'Kabul edilen revizyon',
    incomplete: 'Eksik — bazı satırlar ölçülemedi; toplam bu satırları içermez.',
    unmeasured: 'Ölçülemeyen satırlar',
    tooManyOrders: (count: number) =>
      `Bu grup ${count} siparişe yayılıyor; tek seferde ölçülemedi ve toplamı gösterilmiyor. Sipariş tarih aralığını daraltın.`,
    scanContinues:
      'Bu bölümde açık ya da müşteri onayı bekleyen miktar bulunamadı; tarama sunucuda sınırlı yapılıyor. Devam etmek için sonraki bölümü yükleyin.',
  },
  lost: {
    intro:
      'Kaybedildi olarak kaydedilen teklifler (en son kayıp önce). Neden, Ayarlar listesinden seçilen kayıttır; tutar, teklif satırlarından hesaplanan KDV hariç net tutardır ve yalnız teklifin kendi para biriminde gösterilir. Tarih süzgeci kayıp gününe (İstanbul) uygulanır.',
    subject: 'kayıp fırsatlar',
    caption: 'Kayıp teklifler',
    quote: 'Teklif',
    quoteDate: 'Teklif tarihi',
    lostOn: 'Kayıp günü',
    reason: 'Kayıp nedeni',
    legacyReason: 'Serbest metin (liste öncesi kayıt)',
    explanation: 'Açıklama',
    netAmount: 'Net tutar (KDV hariç)',
    amountState: 'Tutar durumu',
    withheld: 'Tutarlar gizli: fiyat okuma yetkiniz yok.',
    withheldCell: 'Gizli',
    lineCount: 'Satır sayısı',
  },
  priceVariance: {
    intro:
      'Satış faturası birim fiyatının sipariş birim fiyatından sapması. Düşük fiyat uyarı ve sapma kaydıdır; yüksek fiyat sipariş revizyonu gerektirir.',
    subject: 'fiyat sapması',
    noSource:
      'Kaynak yok: satış faturası kaydı bu sürümde henüz yok, bu yüzden fatura–sipariş fiyat sapması hesaplanamaz. Bu "sapma yok" anlamına gelmez.',
    caption: 'Fiyat sapmaları',
    invoice: 'Fatura',
    invoiceDate: 'Fatura tarihi',
    order: 'Sipariş',
    quantity: 'Faturalanan miktar',
    orderPrice: 'Sipariş net birim fiyatı',
    invoicePrice: 'Fatura net birim fiyatı',
    difference: 'Birim fark',
    differenceAmount: 'Fark tutarı',
    direction: 'Yön',
    below: 'Sipariş fiyatının altında',
    above: 'Sipariş fiyatının üstünde',
    empty: 'Sapma kaydı yok.',
  },
} as const

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
  PendingReapproval: 'Revizyon yeniden onay bekliyor; son onaylı miktar tam alınamadı',
  TolerancePolicyUnavailable: 'Eksik sevk edildi ve teslim toleransı (Ayarlar) okunamadı; kalanın açık olup olmadığı bilinmiyor',
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
      'Onaylı, kapatılmamış ve iptal edilmemiş siparişlerin açık miktarı: onaylı miktar − sevk edilen miktar (sıfırın altına inmez). Teslim toleransı içinde eksik sevkle tamamlanmış satırın kalanı açık sayılmaz. Müşteri, ürün ve birim başına gruplanır; farklı birimler hiçbir zaman toplanmaz. Tarih süzgeci sipariş tarihine uygulanır.',
    subject: 'açık siparişler',
    caption: 'Açık sipariş miktarları',
    unit: 'Birim',
    orderCount: 'Sipariş sayısı',
    approved: 'Onaylı miktar',
    shipped: 'Sevk edilen',
    remaining: 'Açık miktar',
    completedWithinTolerance: 'Tolerans içinde tamamlandı',
    detail: 'Sipariş satırları',
    detailCaption: (product: string, customer: string) => `${customer} — ${product} sipariş satırları`,
    order: 'Sipariş',
    orderDate: 'Sipariş tarihi',
    delivery: 'İstenen teslim',
    basisRevision: 'Onaylı revizyon',
    incomplete: 'Eksik — bazı satırlar ölçülemedi; toplam bu satırları içermez.',
    unmeasured: 'Ölçülemeyen satırlar',
    tooWide: 'Bu sayfa çok fazla siparişe yayılıyor. Tarih aralığını ya da müşteriyi daraltın.',
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

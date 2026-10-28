/**
 * UI-60 / UI-61 user texts (Turkish, single source). S-07 "Sevkiyat / İrsaliyeler" and S-08 "Faturaya Hazır" are read-only
 * screens: there is no "Fatura Kes" action (SAT-D7: the official invoice is issued in the external program) and no amount.
 */

import type { DispatchState, GroupKeyPart } from './dispatchContract'

export const DISPATCH_STATE_TEXT: Readonly<Record<DispatchState, string>> = {
  Draft: 'Taslak',
  Approved: 'Onaylı',
  Cancelled: 'İptal',
}

export const GROUP_KEY_PART_TEXT: Readonly<Record<GroupKeyPart, string>> = {
  Company: 'Şirket',
  Customer: 'Müşteri',
  Currency: 'Para birimi',
  Tax: 'Vergi',
  ExchangeRate: 'Kur',
  PaymentTerms: 'Vade / ticari koşul',
  EDocument: 'E-belge uyumu',
}

export const DISPATCH_TEXT = {
  common: {
    customer: 'Müşteri',
    order: 'Sipariş',
    product: 'Ürün',
    quantity: 'Miktar',
    shippedAt: 'Sevk zamanı',
    none: '—',
    identifier: 'Kimlik',
    filterAll: 'Tümü',
    invoicingNoSource:
      'Faturalanma durumu henüz izlenmiyor: fatura kaydı bu sürümde yok, bu yüzden bir pay faturalanmış ya da faturalanmamış gösterilmez.',
    noAmounts: 'Tutar, KDV ve toplam bu ekranda gösterilmez; sipariş toplamları hesaplayıcısı geldiğinde sunucudan okunacak.',
    nav: 'Satış irsaliye ve fatura ekranları',
    dispatchesTab: 'Sevkiyat / İrsaliyeler',
    candidatesTab: 'Faturaya Hazır',
  },
  dispatches: {
    heading: 'Sevkiyat / İrsaliyeler',
    intro: 'Satış yönündeki irsaliyeler (en yenisi önce). Alış kabulleri ve transfer belgeleri bu listede yer almaz.',
    filter: 'Durum',
    caption: 'Satış irsaliyeleri',
    subject: 'satış irsaliyeleri',
    number: 'İrsaliye no',
    eDispatchNumber: 'e-İrsaliye no',
    state: 'Durum',
    createdAt: 'Oluşturma',
    approvedAt: 'Onay zamanı',
    lines: 'Sevk satırları',
    draftNumber: 'Numara onayda verilir',
    lineSummary: (order: string, lineNo: number, product: string, quantity: string, unit: string) =>
      `${order} / satır ${lineNo} — ${product} — ${quantity} ${unit}`,
    loadMore: 'Daha fazla irsaliye yükle',
    loadingMore: 'Yükleniyor…',
    retryMore: 'Sonraki sayfayı yeniden dene',
    end: 'Listenin sonu.',
  },
  candidates: {
    heading: 'Faturaya Hazır',
    intro:
      'Onaylı irsaliyelerin sevk payları, aynı şirket, müşteri, para birimi ve (varsa) vade koşuluna göre en büyük uyumlu grup olarak önerilir. Grubu bölmek ve kısmi miktar seçmek fatura taslağında yapılacak.',
    subject: 'faturaya hazır sevk payları',
    group: (index: number) => `Grup ${index}`,
    customer: 'Müşteri',
    currency: 'Para birimi',
    paymentTerms: 'Vade / ticari koşul',
    paymentTermsNone: 'Siparişte vade koşulu yok',
    noteCount: 'İrsaliye sayısı',
    totals: 'Toplam miktar (birim başına)',
    incomplete: 'Gruplama tamamlanmadı — şu ölçütlerin kaynağı henüz yok:',
    incompleteHint: 'Bu ölçütler tamamlanmadan grup tek faturaya alınabilir sayılmaz.',
    complete: 'Gruplama ölçütleri tamam.',
    sharesCaption: (index: number) => `Grup ${index} sevk payları`,
    dispatch: 'İrsaliye',
    approvedAt: 'Onay zamanı',
    truncated: 'Liste kısaltıldı: daha fazla sevk payı var. Bir müşteriye daraltın; gruplar eksik olabilir.',
    onlyCustomer: 'Yalnız bu müşteri',
    allCustomers: 'Tüm müşteriler',
    filteredTo: (customer: string) => `Yalnız ${customer} gösteriliyor.`,
  },
} as const

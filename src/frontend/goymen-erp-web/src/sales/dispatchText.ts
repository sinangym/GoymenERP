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
  ExchangeRate: 'Kur değeri (sevk gününden önceki iş günü TCMB kuru)',
  InvoiceType: 'Fatura tipi (SATIŞ / TEVKİFAT / İSTİSNA / İHRAÇ KAYITLI)',
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
      'Onaylı irsaliyelerin sevk payları; aynı şirket ve cari hesapta para birimi, kur (sevk gününden önceki iş günü TCMB kuru; TL siparişte yok) ve vade tarihi aynı olanlar müşteri başına en büyük uyumlu grup olarak önerilir. Grubu bölmek ve kısmi miktar seçmek fatura taslağında yapılacak.',
    subject: 'faturaya hazır sevk payları',
    group: (index: number) => `Grup ${index}`,
    customer: 'Müşteri',
    currency: 'Para birimi',
    exchangeRate: 'Kur',
    rateNotApplicable: 'TL sipariş — kur uygulanmaz',
    rateUnresolved: (shipmentDate: string) => `${shipmentDate} sevk gününden önceki iş günü TCMB kuru — kur kaydı henüz yok`,
    rateResolved: (bulletinDate: string, rate: string) => `${rate} (${bulletinDate} TCMB)`,
    dueDate: 'Vade tarihi',
    dueDateMissing: 'Vade eksik — siparişte vade bilgisi yok; bu grup faturalanamaz.',
    invoiceType: 'Fatura tipi',
    sourceMissing: 'Kaynak yok',
    noteCount: 'İrsaliye sayısı',
    totals: 'Toplam miktar (birim başına)',
    incomplete: 'Gruplama tamamlanmadı — şu ölçütlerin kaynağı henüz yok:',
    incompleteHint: 'Bu ölçütler tamamlanmadan grup tek faturaya alınabilir sayılmaz.',
    notInvoiceable: 'Faturalanamaz',
    invoiceable: 'Fatura ölçütleri tamam.',
    sharesCaption: (index: number) => `Grup ${index} sevk payları`,
    dispatch: 'İrsaliye',
    eDispatchNumber: 'e-İrsaliye no',
    eDispatchNumberMissing: 'e-İrsaliye no girilmedi',
    approvedAt: 'Onay zamanı',
    truncated: 'Liste kısaltıldı: daha fazla sevk payı var. Bir müşteriye daraltın; gruplar eksik olabilir.',
    onlyCustomer: 'Yalnız bu müşteri',
    allCustomers: 'Tüm müşteriler',
    filteredTo: (customer: string) => `Yalnız ${customer} gösteriliyor.`,
  },
} as const

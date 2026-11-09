/**
 * UI-50 / S-05 "Fiyat–İskonto–Marj–Risk Kontrolü" — the Turkish texts of the read-only comparison screen. Texts only:
 * every price, net, total and risk figure is the SERVER's (the screen never computes one).
 */

export const PRICE_CONTROL_TEXT = {
  pageLabel: 'Fiyat, iskonto, marj ve risk kontrolü',
  heading: (documentNo: string) => `${documentNo} · Fiyat–İskonto–Marj–Risk Kontrolü`,
  note: 'Bu ekran yalnız karşılaştırma ve önizlemedir. Onay, ret ve fiyat değişikliği siparişin kendi ekranında (Sipariş 360) yapılır.',
  stateSubject: 'fiyat kontrolü',
  backToOrder: 'Siparişe dön',
  openControl: 'Fiyat kontrolü',
  lines: {
    heading: 'Satır fiyat karşılaştırması',
    caption: 'Satır bazında liste fiyatı, taban fiyat, birim fiyat, iskonto, vade farkı ve sunucunun net fiyatı',
    note: 'Liste ve taban fiyatı KDV hariç net referanstır. Net fiyatı, istisna ve limit kontrolünü sunucu hesaplar.',
    line: 'Satır',
    product: 'Ürün',
    quantity: 'Miktar',
    reference: 'Liste fiyatı',
    floor: 'Taban fiyat',
    unitPrice: 'Birim fiyat',
    source: 'Kaynak',
    discount: 'İskonto',
    surcharge: 'Vade farkı',
    net: 'Net birim fiyat',
    floorStatus: 'Taban istisnası',
    discountStatus: 'İskonto limiti',
    withheld: 'Fiyat ayrıntılarını görme yetkiniz yok; karşılaştırma gösterilmiyor.',
    unreadable: 'Satır fiyat ayrıntıları okunamadı.',
    stale: 'Fiyatlar siparişin gösterilen sürümüne ait değil; sipariş siz baktıktan sonra değişmiş olabilir.',
    refresh: 'Siparişi ve fiyatları yeniden oku',
  },
  status: {
    none: '—',
    floorPending: 'Onay bekliyor',
    floorApproved: 'Onaylandı',
    discountWithin: (limit: string) => `Limit içinde (%${limit})`,
    discountNoLimit: 'Limit tanımlı değil — onay gerekir',
    discountUnmeasured: 'Liste fiyatı yok — onay gerekir',
    discountOverPending: (limit: string) => `Limit aşıldı (%${limit}) — onay bekliyor`,
    discountApproved: 'Limit aşımı onaylandı',
  },
  summary: {
    heading: 'Onay gerektiren istisnalar',
    floorPending: 'Onay bekleyen taban fiyat istisnası',
    discountPending: 'Onay bekleyen iskonto limiti aşımı',
  },
  margin: {
    heading: 'Marj',
    unavailable: 'Kullanılamıyor: maliyet ve marj okuma ucu henüz yok. Marj bu ekranda hesaplanmaz ve tahmin edilmez.',
  },
  source: {
    Reference: 'Liste',
    Manual: 'Elle',
  },
  percent: (value: string) => `%${value}`,
  amount: (value: string) => `${value} tutar`,
  priceWithCurrency: (value: string, currency: string) => `${value} ${currency}`,
} as const

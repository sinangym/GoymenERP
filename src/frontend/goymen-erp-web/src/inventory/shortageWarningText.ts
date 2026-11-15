/** KLN-D42 dated shortage warning: every user-facing Turkish text of the screen, in one localizable source. */
import type { ShortageCoverage, ShortageStatus } from './shortageWarningContract'

export const SHORTAGE_WARNING_TEXT = {
  eyebrow: 'Stok',
  title: 'Tarihli eksik uyarıları',
  scope: 'Ürün bazında açık satış kilogramı, rezerve edilmemiş serbest stok ile açık satın alma kilogramının toplamını ' +
    'aşıyorsa uyarı verilir. Üretim emirleri hesaba katılmaz.',
  noRequest: 'Bu ekran yalnız uyarı gösterir; otomatik satın alma talebi, rezervasyon veya stok hareketi oluşturmaz.',
  entryLink: 'Tarihli eksik uyarılarını aç',
  noCompany: 'Yetkili tek şirket bağlamı seçilmeden eksik uyarıları görüntülenemez.',
  subject: 'eksik uyarıları',
  empty: 'Açık satış siparişi olan ürün yok.',
  emptyWarnings: 'Uyarı gerektiren ürün yok: açık satışların tamamı serbest stok ve açık satın almalarla karşılanıyor.',
  invalidQuery: 'Eksik uyarıları isteği geçersiz bulundu. Listeyi yeniden yükleyin.',
  reload: 'Yeniden yükle',
  showAll: 'Karşılanan ürünleri de göster',
  supplyDatesNotAnswered: 'Satın alma teslim tarihleri henüz okunamıyor. Açık satın almayla karşılanan satırlar ' +
    '"tarih bilinmiyor" olarak gösterilir; geç karşılanıp karşılanmadığı söylenemez.',
  unconvertible: 'Bu ürünün açık satış veya satın alma satırlarından biri kilogram cinsinden değil; birim dönüşümü ' +
    'tanımlanmadan toplam hesaplanmaz.',
  noDueDate: 'Termin yok',
  dash: '—',
  linesCaption: (productCode: string) => `${productCode} açık satış satırları (en erken termin önce)`,
  productsCaption: 'Ürün bazında eksik durumu',
  similarTitle: 'Benzer ürün stoku (yalnız bilgi; karşılamaya sayılmaz)',
  similarNotConfigured: 'Benzer ürün kuralı henüz tanımlı değil.',
  similarUnavailable: 'Benzer ürün stoku şu anda okunamadı.',
  similarNone: 'Serbest stoku olan benzer ürün yok.',
  shownCount: (products: number) => `${products} ürün gösteriliyor.`,
  kg: (value: string) => `${value} kg`,
  headers: {
    product: 'Ürün',
    status: 'Durum',
    earliestDue: 'En erken termin',
    openDemand: 'Açık satış',
    reserved: 'Rezerve',
    netDemand: 'Net ihtiyaç',
    freeStock: 'Serbest stok',
    openSupply: 'Açık satın alma',
    shortage: 'Eksik',
  },
  lineHeaders: {
    document: 'Sipariş / satır',
    dueDate: 'Termin',
    open: 'Açık',
    reserved: 'Rezerve',
    fromStock: 'Stoktan',
    fromSupply: 'Satın almadan',
    uncovered: 'Karşılanmayan',
    coverage: 'Karşılama',
    latestSupply: 'Son satın alma tarihi',
  },
  similarHeaders: {
    product: 'Benzer ürün',
    freeStock: 'Serbest stok',
  },
} as const

export const SHORTAGE_STATUS_TEXT: Readonly<Record<ShortageStatus, string>> = {
  Covered: 'Karşılanıyor',
  CoverageDateUnknown: 'Satın alma tarihi bilinmiyor',
  LateCovered: 'Geç karşılanıyor',
  Shortage: 'Eksik var',
  Unconvertible: 'Hesaplanamadı (birim)',
}

export const SHORTAGE_COVERAGE_TEXT: Readonly<Record<ShortageCoverage, string>> = {
  Covered: 'Karşılanıyor',
  CoverageDateUnknown: 'Tarih bilinmiyor',
  LateCovered: 'Geç karşılanıyor',
  Uncovered: 'Karşılanmıyor',
}

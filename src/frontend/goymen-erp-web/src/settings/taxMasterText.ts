/**
 * Ayarlar > Vergi (KDV / tevkifat) — every user-facing text of the tax master screen (MOD-03, SAT-D73/D74/D82/D83, EK13).
 * Kept here, never inline, so the screen stays localizable.
 */

import { TAX_PROBLEM, type CategoryKind, type CodeListKind } from './taxMasterContract'

export const TAX_TEXT = {
  title: 'Vergi ayarları (KDV ve tevkifat)',
  intro:
    'KDV sınıfları ve oranları, tevkifat sınıfları ve alıcı kategorileri, istisna ve tevkifat kod listeleri. Oranlar ve kodlar mali müşavir teyidiyle girilir; değer girilmeden ilgili alan "kaynak yok" kalır ve sipariş satırında vergi hesaplanmaz.',
  contextSubject: 'vergi ayarları',
  masterSubject: 'vergi ayarları',
  noSource: 'Kaynak yok',
  emptyMaster: 'Henüz vergi kaydı girilmedi; tüm KDV ve tevkifat alanları "kaynak yok" durumunda.',
  vatHeading: 'KDV sınıfları ve oranları',
  vatTableLabel: 'KDV sınıfları',
  withholdingHeading: 'Tevkifat (sınıf × alıcı kategorisi)',
  withholdingTableLabel: 'Tevkifat oranları',
  withholdingNeedsCategories: 'Tevkifat oranı için en az bir tevkifat sınıfı ve bir alıcı kategorisi tanımlanmalıdır.',
  buyerHeading: 'Alıcı kategorileri',
  codeListsHeading: 'UBL-TR kod listeleri',
  categoriesHeading: 'Yeni kategori',
  columnCode: 'Kod',
  columnName: 'Ad',
  columnInForce: 'Bugün geçerli',
  columnScheduled: 'Planlı',
  columnActions: 'İşlemler',
  columnClass: 'Tevkifat sınıfı',
  columnBuyer: 'Alıcı kategorisi',
  noScheduled: '—',
  noCategoriesOfKind: 'Bu türde kategori yok.',
  newCategory: 'Yeni kategori',
  recordRate: 'Oran gir',
  recordWithholding: 'Tevkifat gir',
  withdraw: 'Geri çek',
  publishList: 'Yeni liste sürümü',
  kindLabel: 'Tür',
  codeLabel: 'Kod',
  codeHelp: 'Büyük harf (A-Z), rakam, "_" ve "-"; en çok 32 karakter.',
  nameLabel: 'Ad',
  rateLabel: 'Oran (%)',
  rateHelp: '0-100 arası, en çok 4 ondalık (örn. 20 veya 0.5).',
  withholdingRateHelp: "Tevkif edilen KDV payı yüzde olarak (örn. 5/10 için 50); 0'dan büyük, en fazla 100.",
  appliesLabel: 'Tevkifat uygulanır',
  notAppliedLabel: 'Bu alıcı kategorisine tevkifat uygulanmaz',
  withholdingCodeLabel: 'Tevkifat kodu',
  withholdingCodeHelp: 'Yürürlükteki tevkifat kod listesinden bir kod.',
  buyerLabel: 'Alıcı kategorisi',
  effectiveFromLabel: 'Geçerlilik başlangıcı',
  effectiveFromHelp: 'Boş bırakılırsa bugün. İleri bir tarih o gün kendiliğinden geçerli olur; aynı tarihli planlı kayıt varsa onun yerine geçer.',
  entriesLabel: 'Kodlar (her satıra "KOD;Ad")',
  entriesHelp: 'Liste bütün olarak yeni sürüm olur; en çok 500 kod, kod yalnız büyük harf ve rakam.',
  entriesInvalid: 'Her satır "KOD;Ad" biçiminde olmalı; kodlar tekrar etmemeli.',
  reasonLabel: 'Gerekçe',
  reasonHelp: 'Zorunlu; değişiklik kaydında saklanır.',
  save: 'Kaydet',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  cancel: 'Vazgeç',
  saved: 'Kaydedildi.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; aynı sonuç gösteriliyor.',
  unknownOutcome:
    'İşlemin sonucu bilinmiyor (bağlantı veya sunucu). Aynı işlemi yeniden deneyin; iki kez uygulanmaz.',
  notApplied: 'Uygulanmaz',
  entryCount: (count: number) => `${count} kod`,
  rateLabelOf: (percent: string) => `%${percent}`,
  withholdingLabelOf: (percent: string, code: string) => `%${percent} (kod ${code})`,
  scheduledLabel: (date: string, value: string) => `${formatDate(date)} itibarıyla ${value}`,
  businessDate: (date: string) => `İş günü: ${formatDate(date)}`,
} as const

export const CATEGORY_KIND_LABEL: Record<CategoryKind, string> = {
  VatClass: 'KDV sınıfı',
  WithholdingClass: 'Tevkifat sınıfı',
  BuyerCategory: 'Alıcı kategorisi',
}

export const CODE_LIST_LABEL: Record<CodeListKind, string> = {
  Exemption: 'KDV istisna kodları',
  Withholding: 'KDV tevkifat kodları',
}

/** dd.MM.yyyy from the wire's yyyy-MM-dd business date (no time zone arithmetic). */
export function formatDate(date: string): string {
  const [year, month, day] = date.split('-')
  return `${day}.${month}.${year}`
}

export function taxRefusalText(status: number, code: string | null): string {
  switch (code) {
    case TAX_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case TAX_PROBLEM.reasonTooLong:
      return 'Gerekçe en fazla 4000 karakter olabilir.'
    case TAX_PROBLEM.versionConflict:
      return 'Kayıt siz açtıktan sonra değişti; güncel hâli yüklendi, yeniden deneyin.'
    case TAX_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açın.'
    case TAX_PROBLEM.categoryCodeTaken:
      return 'Bu türde aynı kodla bir kategori zaten var.'
    case TAX_PROBLEM.categoryNameTaken:
      return 'Bu türde aynı adla bir kategori zaten var.'
    case TAX_PROBLEM.categoryKindMismatch:
      return 'Bu kayıt yalnız ilgili türdeki sınıfa girilir.'
    case TAX_PROBLEM.buyerCategoryNotFound:
      return 'Alıcı kategorisi bu şirkette yok.'
    case TAX_PROBLEM.withholdingCodeUnknown:
      return 'Tevkifat kodu, kaydın başladığı gün yürürlükteki tevkifat kod listesinde yok.'
    case TAX_PROBLEM.effectiveFromPast:
      return 'Geçerlilik başlangıcı bugünden önce olamaz.'
    case TAX_PROBLEM.recordInEffect:
      return 'Yürürlüğe girmiş kayıt geri çekilemez; yeni bir kayıt girin.'
    case TAX_PROBLEM.recordWithdrawn:
      return 'Bu kayıt daha önce geri çekildi.'
    case TAX_PROBLEM.requestInvalid:
      return 'Girilen değerler geçersiz.'
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok.'
      if (status === 404) return 'Kayıt bu şirkette bulunamadı.'
      return 'İşlem reddedildi.'
  }
}

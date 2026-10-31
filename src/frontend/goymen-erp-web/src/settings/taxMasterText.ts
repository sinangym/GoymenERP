/**
 * Ayarlar > Vergi (KDV / tevkifat) — every user-facing text of the tax master screen (MOD-03; EK19 SAT-D133…D139).
 * Kept here, never inline, so the screen stays localizable.
 */

import { TAX_PROBLEM, type CodeListKind, type ItemType } from './taxMasterContract'

export const TAX_TEXT = {
  title: 'Vergi ayarları (KDV ve tevkifat)',
  intro:
    'Kalem tipine göre varsayılan KDV ve tevkifat (demir, hurda, hizmet), istisna ve tevkifat kod listeleri ve tevkifat tutar sınırı. Varsayılan sipariş satırında değiştirilebilir; kodlar ve sınır mali müşavir teyidiyle girilir, girilmeden "kaynak yok" kalır.',
  contextSubject: 'vergi ayarları',
  masterSubject: 'vergi ayarları',
  noSource: 'Kaynak yok',
  defaultsHeading: 'Kalem tipine göre varsayılan vergi',
  defaultsTableLabel: 'Kalem tipi varsayılanları',
  columnItemType: 'Kalem tipi',
  columnVat: 'KDV',
  columnWithholding: 'Tevkifat',
  columnSource: 'Kaynak',
  columnInForce: 'Bugün geçerli',
  columnScheduled: 'Planlı',
  columnActions: 'İşlemler',
  noScheduled: '—',
  sourceCatalog: 'Onaylı varsayılan (SAT-D139)',
  sourceRecord: 'Ayarlar kaydı',
  vatNone: 'KDV yok',
  withholdingNone: 'Tevkifat yok',
  codeMissing: 'kod: kaynak yok',
  changeDefault: 'Varsayılanı değiştir',
  withdraw: 'Geri çek',
  vatAppliesLabel: 'KDV uygulanır',
  vatNotAppliedLabel: 'KDV yok (istisna)',
  vatRateLabel: 'KDV oranı (%)',
  rateHelp: '0-100 arası, en çok 4 ondalık (örn. 20 veya 0.5).',
  exemptionCodeLabel: 'İstisna kodu (isteğe bağlı)',
  exemptionCodeHelp: 'Yürürlükteki istisna kod listesinden; boş bırakılırsa "kaynak yok" kalır ve fatura bekler.',
  withholdingAppliesLabel: 'Tevkifat uygulanır',
  withholdingRateLabel: 'Tevkifat oranı (%)',
  withholdingRateHelp: "Tevkif edilen KDV payı yüzde olarak (örn. 5/10 için 50); 0'dan büyük, en fazla 100.",
  withholdingCodeLabel: 'Tevkifat kodu (isteğe bağlı)',
  withholdingCodeHelp: 'Yürürlükteki tevkifat kod listesinden; boş bırakılırsa "kaynak yok" kalır ve fatura bekler.',
  codeListsHeading: 'UBL-TR kod listeleri',
  publishList: 'Yeni liste sürümü',
  thresholdHeading: 'Tevkifat tutar sınırı (VUK 232)',
  thresholdIntro:
    'Kısmi tevkifatın uygulanacağı işlem bedeli sınırı (KDV dahil, işlem bütünü). Mali müşavir teyidiyle girilir; girilmeden tevkifatlı fatura "kaynak yok" ile bekler.',
  recordThreshold: 'Sınır gir',
  amountLabel: 'Tutar',
  amountHelp: "0'dan büyük, en çok 15 tam ve 4 ondalık basamak (örn. 12000 veya 12000.50).",
  currencyLabel: 'Para birimi',
  currencyHelp: 'Üç harfli ISO 4217 kodu (örn. TRY); otomatik seçilmez.',
  effectiveFromLabel: 'Geçerlilik başlangıcı',
  effectiveFromHelp:
    'Boş bırakılırsa bugün. İleri bir tarih o gün kendiliğinden geçerli olur; aynı tarihli planlı kayıt varsa onun yerine geçer.',
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
  unknownOutcome: 'İşlemin sonucu bilinmiyor (bağlantı veya sunucu). Aynı işlemi yeniden deneyin; iki kez uygulanmaz.',
  entryCount: (count: number) => `${count} kod`,
  rateLabelOf: (percent: string) => `%${percent}`,
  codeOf: (code: string) => `kod ${code}`,
  thresholdLabelOf: (amount: string, currency: string) => `${amount} ${currency}`,
  scheduledLabel: (date: string, value: string) => `${formatDate(date)} itibarıyla ${value}`,
  businessDate: (date: string) => `İş günü: ${formatDate(date)}`,
} as const

export const ITEM_TYPE_LABEL: Record<ItemType, string> = {
  Steel: 'Demir',
  Scrap: 'Hurda',
  Service: 'Hizmet',
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
    case TAX_PROBLEM.exemptionCodeUnknown:
      return 'İstisna kodu, kaydın başladığı gün yürürlükteki istisna kod listesinde yok.'
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

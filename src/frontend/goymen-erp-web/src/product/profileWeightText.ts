/** KLN-D54 (STK-S37): the texts of the structural profile weight table screen (localizable; never inline). */

import type { ParseRefusal } from './profileWeightApi'

export const PROFILE_WEIGHT_TEXT = {
  heading: 'Yapısal profil ağırlık tablosu (kg/m)',
  intro:
    'UPN, NPI, IPE, HEA, HEB ve HEM profillerinin metre ağırlığı bu tablodan okunur; ürün kartında elle girilmez. Tablo, eldeki standart veya katalogdan kaynağı yazılarak içe aktarılır; her içe aktarma yeni bir sürümdür ve denetim kaydına yazılır.',
  loading: 'Ağırlık tablosu yükleniyor…',
  failed: 'Ağırlık tablosu şu anda okunamadı.',
  unavailable: 'Ağırlık tablosu hizmeti şu anda kullanılamıyor.',
  unauthenticated: 'Oturumunuz sona erdi; ağırlık tablosunu görmek için yeniden giriş yapın.',
  retryRead: 'Yeniden dene',
  none: 'Henüz tablo içe aktarılmadı — yapısal profillerin metre ağırlığı "kaynak yok" olarak görünür, tahmin edilmez.',
  versionLabel: 'Sürüm',
  sourceLabel: 'Kaynak',
  reasonShownLabel: 'Gerekçe',
  importedAtLabel: 'İçe aktarma zamanı',
  importedByLabel: 'İçe aktaran',
  rowCountLabel: 'Satır sayısı',
  tableCaption: 'Güncel tablo',
  seriesColumn: 'Seri',
  sizeColumn: 'Ölçü',
  weightColumn: 'kg/m',
  importHeading: 'Yeni sürüm içe aktar',
  formatNote:
    'Dosya UTF-8 metin olmalıdır. İlk satır "series;size;kgPerMetre" başlığıdır; sonraki her satır bir profildir, alanlar ";" ile ayrılır ve ondalık ayırıcı noktadır (ör. IPE;200;22.4). Dosyanın kendisi sunucuya yüklenmez; yalnız satırlar gönderilir.',
  fileLabel: 'Tablo dosyası',
  fileUnreadable: 'Dosya okunamadı.',
  parsedRows: (count: number) => `${count} satır okundu.`,
  sourceInputLabel: 'Kaynak (standart / katalog ve baskısı)',
  sourceHint: 'Örneğin standart adı ve yılı veya tedarikçi kataloğu ve baskısı. Kaynak zorunludur ve tabloyla birlikte saklanır.',
  reasonLabel: 'Gerekçe',
  submit: 'İçe aktar',
  submitting: 'İçe aktarılıyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  imported: 'Ağırlık tablosu içe aktarıldı.',
  replayed: 'Bu içe aktarma daha önce uygulanmıştı; ikinci kez uygulanmadı.',
  conflict: 'Tablo siz açtıktan sonra değişti; güncel sürüm yüklendi. Dosyayı yeniden seçip içe aktarın.',
  fileRequired: 'Önce geçerli bir tablo dosyası seçin.',
  sourceRequired: 'Kaynak zorunludur.',
  sourceTooLong: 'Kaynak en fazla 200 karakter olabilir.',
  reasonRequired: 'Gerekçe zorunludur.',
  reasonTooLong: 'Gerekçe en fazla 4000 karakter olabilir.',
  rowsRefused: 'Tablo satırları sunucu tarafından reddedildi.',
  refused: 'Ağırlık tablosu içe aktarılamadı.',
  unknownOutcome: 'İşlemin sonucu bilinmiyor. Aynı işlemi yeniden deneyin; iki kez uygulanmaz.',
} as const

export const PARSE_REFUSAL_TEXT: Record<ParseRefusal, string> = {
  header: 'İlk satır tam olarak "series;size;kgPerMetre" olmalıdır.',
  empty: 'Dosyada başlıktan sonra en az bir profil satırı olmalıdır.',
  tooMany: 'Dosyada en fazla 2000 profil satırı olabilir.',
  columns: 'Satırda ";" ile ayrılmış tam üç alan olmalıdır.',
  series: 'Seri UPN, NPI, IPE, HEA, HEB veya HEM olmalıdır.',
  size: 'Ölçü adı en fazla 16 karakter büyük harf, rakam ve nokta olmalıdır.',
  weight: 'Metre ağırlığı sıfırdan büyük, nokta ondalıklı ve en fazla 6 ondalıklı olmalıdır.',
  duplicate: 'Aynı seri ve ölçü dosyada ikinci kez geçiyor.',
}

export function parseRefusalMessage(reason: ParseRefusal, line: number | null): string {
  return line === null ? PARSE_REFUSAL_TEXT[reason] : `${line}. satır: ${PARSE_REFUSAL_TEXT[reason]}`
}

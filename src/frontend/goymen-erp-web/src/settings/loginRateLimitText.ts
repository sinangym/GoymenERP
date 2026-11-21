/**
 * Turkish texts of the Ayarlar > Giriş deneme sınırı screen (KIM-D34). Every user-facing string lives here; the screen
 * composes nothing from a server's localized title or detail and never states a limit the server did not send.
 */

import {
  LOGIN_RATE_LIMIT_INPUT,
  LOGIN_RATE_LIMIT_PARTITION,
  LOGIN_RATE_LIMIT_PROBLEM,
} from './loginRateLimitContract'

export const LOGIN_RATE_LIMIT_TEXT = {
  title: 'Giriş deneme sınırı',
  intro:
    'Yerel hesapla girişte bir zaman penceresinde izin verilen deneme sayısı. Değer grup genelidir: bütün şirketlerin ' +
    'girişi aynı sınırı kullanır. Kaydedilen sürüm, giriş tarafının bir sonraki yenilemesinde yürürlüğe girer.',
  subject: 'giriş deneme sınırı',
  tableLabel: 'Giriş deneme sınırı sürümleri',
  empty:
    'Henüz kayıtlı sürüm yok. Bu durumda girişte sunucunun yapılandırmasındaki başlangıç değeri uygulanır; ' +
    'ilk sürümü kaydettiğinizde Ayarlar kaydı geçerli olur.',
  currentHeading: 'Yürürlükte',
  noneCurrent: 'Kayıtlı sürüm olmadığı için yapılandırmadaki başlangıç değeri uygulanıyor.',
  columnVersion: 'Sürüm',
  columnPermit: 'Deneme sayısı',
  columnWindow: 'Pencere',
  columnPartition: 'Sayım birimi',
  columnTrustedProxies: 'Güvenilir vekil sunucular',
  columnCurrent: 'Durum',
  columnRecorded: 'Kaydeden / zaman',
  columnRecordedIn: 'Kaydedildiği şirket',
  columnReason: 'Gerekçe',
  isCurrent: 'Yürürlükte',
  isPrevious: 'Önceki sürüm',
  decisionHint:
    'Karar KIM-D34’teki başlangıç değeri: dakikada 10 deneme, IP adresi + normalleştirilmiş kullanıcı adı başına. ' +
    'Bu bilgi yalnız hatırlatmadır; form değeri kendiliğinden doldurmaz.',
  groupWideNote:
    'Bu değer grup genelidir ve ikinci bir onaya gitmez; yetkili kişi kaydettiği anda yayımlanır. Her değişiklik, ' +
    'kaydın yapıldığı şirketle birlikte denetim kaydına geçer.',
  newVersion: 'Yeni sürüm kaydet',
  permitLabel: 'Deneme sayısı (pencere başına)',
  permitHelp: `Tam sayı; ${LOGIN_RATE_LIMIT_INPUT.minimumPermitLimit} ile ${LOGIN_RATE_LIMIT_INPUT.maximumPermitLimit} arasında.`,
  permitError: `${LOGIN_RATE_LIMIT_INPUT.minimumPermitLimit} ile ${LOGIN_RATE_LIMIT_INPUT.maximumPermitLimit} arasında bir tam sayı girin.`,
  windowLabel: 'Pencere süresi (saniye)',
  windowHelp: `Tam sayı; ${LOGIN_RATE_LIMIT_INPUT.minimumWindowSeconds} ile ${LOGIN_RATE_LIMIT_INPUT.maximumWindowSeconds} saniye arasında (örn. bir dakika için 60).`,
  windowError: `${LOGIN_RATE_LIMIT_INPUT.minimumWindowSeconds} ile ${LOGIN_RATE_LIMIT_INPUT.maximumWindowSeconds} arasında bir tam sayı girin.`,
  partitionLegend: 'Sayım birimi',
  trustedProxiesLabel: 'Güvenilir vekil sunucular (IP adresi veya CIDR bloğu)',
  trustedProxiesHelp:
    `Her satıra bir adres ya da blok yazın (örn. 192.0.2.10 veya 198.51.100.0/24); en çok ` +
    `${LOGIN_RATE_LIMIT_INPUT.maximumTrustedProxyCount} kayıt. Boş bırakırsanız X-Forwarded-For başlığı hiç okunmaz ` +
    've doğrudan bağlanan adres sayılır. Başlık yalnız doğrudan bağlanan adres bu listedeyse okunur; zincirde ' +
    'güvenilir vekillerden sonraki ilk adres istemci sayılır.',
  trustedProxiesError: 'Her satır bir IP adresi veya CIDR bloğu olmalı; aynı kayıt iki kez yazılamaz.',
  trustedProxiesNone: 'Yok (başlık okunmaz)',
  reasonLabel: 'Gerekçe',
  reasonHelp: `Zorunlu; en çok ${LOGIN_RATE_LIMIT_INPUT.reasonMaximumLength} karakter. Değişiklik gerekçesiyle birlikte denetim kaydına geçer.`,
  save: 'Kaydet',
  cancel: 'Vazgeç',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  saved: 'Değişiklik kaydedildi. Giriş tarafı yeni değeri bir sonraki yenilemesinde uygular.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; sonuç yeniden gösterildi.',
  unknownOutcome:
    'Sonuç alınamadı; işlem uygulanmış olabilir. "Aynı işlemi yeniden dene" aynı işlem anahtarıyla gönderir ve iki kez uygulanmaz.',
  staleReloaded: 'Kayıt siz açtıktan sonra değişti. Güncel hâli yüklendi; değişikliği yeniden yapın.',
} as const

export const LOGIN_RATE_LIMIT_PARTITION_LABEL: Record<string, string> = {
  [LOGIN_RATE_LIMIT_PARTITION.ipAddress]: 'IP adresi başına',
  [LOGIN_RATE_LIMIT_PARTITION.ipAddressAndUserName]: 'IP adresi + kullanıcı adı başına',
}

export function partitionLabel(partition: string): string {
  return LOGIN_RATE_LIMIT_PARTITION_LABEL[partition] ?? partition
}

/** A window in seconds as a reader says it: "60 sn". */
export function windowLabel(seconds: number): string {
  return `${seconds} sn`
}

/** A refusal's text by its stable code (or status when the body is code-less). */
export function loginRateLimitRefusalText(status: number, code: string | null): string {
  switch (code) {
    case LOGIN_RATE_LIMIT_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case LOGIN_RATE_LIMIT_PROBLEM.reasonTooLong:
      return `Gerekçe en fazla ${LOGIN_RATE_LIMIT_INPUT.reasonMaximumLength} karakter olabilir.`
    case LOGIN_RATE_LIMIT_PROBLEM.requestInvalid:
      return 'Girilen değerler geçersiz; alanları kontrol edin.'
    case LOGIN_RATE_LIMIT_PROBLEM.commandMetadataInvalid:
      return 'İstek bilgileri eksik ya da geçersiz; sayfayı yenileyip tekrar deneyin.'
    case LOGIN_RATE_LIMIT_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açıp tekrar deneyin.'
    case LOGIN_RATE_LIMIT_PROBLEM.versionConflict:
      return LOGIN_RATE_LIMIT_TEXT.staleReloaded
    case LOGIN_RATE_LIMIT_PROBLEM.providerUnavailable:
      return 'Ayarlar hizmeti şu anda yanıt vermiyor; biraz sonra tekrar deneyin.'
    case LOGIN_RATE_LIMIT_PROBLEM.profileInvalid:
      return 'Kayıtlı değer okunamadı; sistem yöneticisine bildirin.'
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok.'
      if (status === 415) return 'İstek biçimi kabul edilmedi.'
      return 'İşlem kabul edilmedi.'
  }
}

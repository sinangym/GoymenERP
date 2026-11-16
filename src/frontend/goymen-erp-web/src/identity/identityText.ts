/**
 * User-facing Turkish text of "Kullanıcılar ve yetkiler" (AT-004), in one file so specs and tests read the same
 * strings the screens render. Technical names (role and permission codes) are shown as-is, never translated.
 */

import type { ScopeType, Validity } from './contract'

export const ACCESS_TEXT = {
  heading: 'Kullanıcılar ve yetkiler',
  lead:
    'Seçili şirketteki rol atamaları, yetkiler, kapsamlar, geçerlilik ve onaylayan. Yetki işlemleri kendi izinleriyle açılır.',
  usersHeading: 'Kullanıcılar',
  usersSubject: 'kullanıcı listesi',
  rolesHeading: 'Roller',
  rolesSubject: 'rol listesi',
  detailSubject: 'kullanıcı yetkileri',
  contextSubject: 'kullanıcılar ve yetkiler',
  selectPrompt: 'Rolleri, yetkileri ve kapsamları görmek için bir kullanıcı seçin.',
  assignmentsHeading: 'Rol atamaları',
  effectiveHeading: 'Şu an geçerli yetkiler',
  noEffective: 'Bu kullanıcının bu şirkette şu an geçerli yetkisi yok.',
  passiveUser: 'Pasif kullanıcı',
  passiveRole: 'Pasif rol',
  systemRole: 'Sistem rolü',
  critical: 'Kritik',
  columnRole: 'Rol',
  columnValidity: 'Durum',
  columnFrom: 'Başlangıç',
  columnUntil: 'Bitiş',
  columnScopes: 'Kapsam',
  columnApprover: 'Onaylayan',
  columnPermissions: 'Yetki sayısı',
  columnHolders: 'Bu şirkette sahipleri',
  columnUser: 'Kullanıcı',
  openEnded: 'Süresiz',
  noApprover: 'Kayıtlı onaylayan yok',
  grantedBy: 'Veren rol',
  readOnlyNote: 'Her yetki işlemi sunucuda yeniden denetlenir; görünür bir düğme tek başına yetki vermez.',
} as const

export const ROLE_TEXT = {
  subject: 'rol',
  back: 'Kullanıcılar ve yetkiler',
  permissionsHeading: 'Rolün yetkileri',
  noPermissions: 'Bu rolde yetki yok.',
  holdersHeading: 'Bu şirketteki sahipleri',
  noHolders: 'Bu rol bu şirkette kimseye atanmamış.',
  codeLabel: 'Kod',
} as const

/**
 * ID-1F sign-in and sign-out (pre-authentication screen and the toolbar control). The refusal text is ONE message for
 * a wrong password, an unknown user, a passive and a locked account (KIM-D2/D4): it must never hint which it was, and
 * the lock note is general — it names no duration, because the lock values are Settings values (KIM-D5).
 */
export const ACCESS_GRANT_TEXT = {
  title: 'Yetki işlemleri', queue: 'Bekleyen Critical rol talepleri', queueSubject: 'yetki talepleri', empty: 'Bekleyen talep yok.',
  assign: 'Rol ata', request: 'Onaya gönder', critical: 'Bu rol ikinci bir kişinin onayını gerektirir. Kararı sunucu doğrular.',
  role: 'Atanacak rol', chooseRole: 'Rol seçin', reason: 'Gerekçe', from: 'Başlangıç zamanı (boş: şimdi)', until: 'Bitiş zamanı (boş: süresiz)',
  approve: 'Onayla', reject: 'Reddet', withdraw: 'Geri çek', revoke: 'Atamayı geri al', reasonCode: 'Ret gerekçe kodu', reasonCodeHelp: 'Küçük ASCII harfle başlayın; harf, sayı, nokta, alt çizgi veya kısa çizgi kullanın. En fazla 64 karakter.',
  secondPerson: 'Kendi açtığınız veya sizi hedefleyen talebi onaylayamaz ya da reddedemezsiniz.',
  lastAdmin: 'Şirketin son etkin Admin ataması geri alınamaz. Önce başka bir etkin Admin bulunmalıdır.',
  lastAssignment: 'Bu, kullanıcının şirketteki son etkin ataması. Geri alınırsa bu şirketten parola sıfırlama, kilit açma veya pasifleştirme yapılamaz.',
  succeeded: 'İşlem kaydedildi.', replay: 'Önceki işlemin sonucu alındı.', cancel: 'Vazgeç', confirm: 'İşlemi uygula', pending: 'Kaydediliyor…', retry: 'Aynı işlemi yeniden dene',
  unknown: 'İşlemin sonucu bilinmiyor. Aynı istek ve işlem anahtarıyla yeniden deneyin.', refused: 'İşlem reddedildi. Yetkiyi ve kaydın güncel durumunu kontrol edin.',
  versionConflict: 'Kayıt değişti. Güncel liste yeniden okundu; işlemi yeniden başlatın.', secondApproval: 'Bu rol için ikinci kişi onayı gerekir. Atama yerine onaya gönderin.', selfApproval: 'Talebi açan veya hedef kişi karar veremez.',
  operation: 'İşlem', target: 'Hedef kullanıcı', requester: 'Talebi açan', validity: 'Geçerlilik', preview: 'Atama önizlemesi', noRole: 'Atanabilir etkin rol yok.',
} as const

export const LOGIN_TEXT = {
  heading: 'Giriş yap',
  lead: 'Göymen ERP hesabınızla giriş yapın.',
  userNameLabel: 'Kullanıcı adı',
  passwordLabel: 'Parola',
  showPassword: 'Parolayı göster',
  hidePassword: 'Parolayı gizle',
  submit: 'Giriş yap',
  submitting: 'Giriş yapılıyor…',
  missingFields: 'Kullanıcı adını ve parolayı girin.',
  refused: 'Kullanıcı adı veya parola hatalı',
  lockNote:
    'Art arda hatalı denemelerden sonra giriş bir süre kapalı kalabilir. Bu durumda bir süre sonra yeniden deneyin ' +
    'veya yöneticinize başvurun.',
  rateLimitedTitle: 'Çok fazla giriş denemesi',
  rateLimited: 'Kısa süre içinde çok fazla giriş denemesi yapıldı. Lütfen biraz sonra yeniden deneyin.',
  notEnabledTitle: 'Yerel giriş kullanılamıyor',
  notEnabled: 'Bu ortamda kullanıcı adı ve parolayla giriş etkin değil. Sistem yöneticinize başvurun.',
  requestRejectedTitle: 'İstek doğrulanamadı',
  requestRejected: 'Giriş isteği sunucu tarafından reddedildi. Sayfayı yenileyip yeniden deneyin.',
  serviceUnavailableTitle: 'Servis geçici olarak kullanılamıyor',
  serviceUnavailable: 'Giriş şu anda yapılamıyor. Kısa süre sonra yeniden deneyin.',
  offlineTitle: 'Bağlantı yok',
  offline: 'İnternet bağlantısı geldiğinde yeniden deneyin.',
  networkErrorTitle: 'Sunucuya ulaşılamadı',
  networkError: 'İstek tamamlanamadı ve yanıt alınamadı. Yeniden deneyin.',
  unexpectedTitle: 'Beklenmeyen yanıt',
  unexpected: 'Sunucu yanıtı beklenen biçimde değil. Giriş güvenli biçimde durduruldu.',
  expiredTitle: 'Oturumunuz sona erdi',
  expired: 'Oturumunuz kapandı veya süresi doldu. Devam etmek için yeniden giriş yapın.',
  signedOutTitle: 'Çıkış yapıldı',
  signedOut: 'Oturumunuz kapatıldı. Bu hesaptaki diğer açık oturumlar da sona erdi.',
} as const

export const LOGOUT_TEXT = {
  action: 'Çıkış yap',
  pending: 'Çıkış yapılıyor…',
  notEnabled: 'Bu ortamda çıkış yapılamıyor; yerel giriş etkin değil.',
  failed: 'Çıkış tamamlanamadı; oturumunuz hâlâ açık olabilir. Yeniden deneyin.',
  offline: 'Bağlantı yok; çıkış yapılamadı. Oturumunuz hâlâ açık olabilir.',
  unexpected: 'Sunucu yanıtı beklenen biçimde değil; çıkışın tamamlandığı doğrulanamadı. Yeniden deneyin.',
} as const

export const PASSWORD_TEXT = {
  heading: 'Parolanızı değiştirin',
  lead: 'Devam etmek için yeni bir parola belirlemeniz gerekiyor.',
  current: 'Mevcut parola', next: 'Yeni parola', confirm: 'Yeni parola tekrar',
  show: 'Parolaları göster', hide: 'Parolaları gizle', submit: 'Parolayı değiştir', pending: 'Değiştiriliyor…',
  missing: 'Parola alanlarının tamamını doldurun.', mismatch: 'Yeni parolalar aynı olmalıdır.',
  currentInvalid: 'Mevcut parola doğrulanamadı. Yeniden girin.',
  refused: 'Yeni parola veya istek sunucu tarafından kabul edilmedi. Bilgileri yeniden girin.',
  conflict: 'Hesap bilgileri değişti. Güncel bilgileri yükleyip yeniden deneyin.',
  forbidden: 'Parola değiştirme isteği için yetkiniz yok.', notFound: 'Bu ortamda parola değiştirme kullanılamıyor.',
  offline: 'Bağlantı yok. Sonuç doğrulanamadı; aynı isteği yeniden deneyin.',
  'network-error': 'Sunucuya ulaşılamadı. Sonuç doğrulanamadı; aynı isteği yeniden deneyin.',
  'service-unavailable': 'Servis geçici olarak kullanılamıyor. Sonuç doğrulanamadı; aynı isteği yeniden deneyin.',
  malformed: 'Beklenmeyen yanıt alındı. Sonuç doğrulanamadı; aynı isteği yeniden deneyin.',
  retry: 'Aynı isteği yeniden dene', reload: 'Güncel bilgileri yükle',
} as const

export function assignmentCountLabel(effective: number, total: number): string {
  return `${effective} geçerli / ${total} atama`
}

export function holderCountLabel(effective: number, total: number): string {
  return `${effective} geçerli / ${total} kişi`
}

export const VALIDITY_LABEL: Record<Validity, string> = {
  effective: 'Geçerli',
  scheduled: 'Henüz başlamadı',
  ended: 'Süresi doldu',
  revoked: 'Geri alındı',
  inactive: 'Etkin değil',
}

const SCOPE_LABEL: Record<ScopeType, string> = {
  Company: 'Şirket geneli',
  Site: 'Tesis',
  Location: 'Lokasyon',
  WorkCenter: 'İş merkezi',
}

export function scopeLabel(type: ScopeType): string {
  return SCOPE_LABEL[type]
}

const DATE = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Istanbul',
})

/** Business display in Türkiye time; the stored instant is UTC and is never re-derived here. */
export function formatInstant(value: string): string {
  return DATE.format(new Date(value))
}

const COLLATOR = new Intl.Collator('tr', { sensitivity: 'base' })

export function compareTurkish(left: string, right: string): number {
  return COLLATOR.compare(left, right)
}

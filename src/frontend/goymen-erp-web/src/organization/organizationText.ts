/**
 * Turkish user-facing text for the Ayarlar/Organizasyon screens.
 *
 * Collected here so the screens hold no loose strings and so the one rule that
 * matters stays checkable: WE NEVER PARSE SERVER TEXT, AND WE NEVER WRITE TEXT THAT
 * CLAIMS MORE THAN WE KNOW. Every message below is chosen from a status code or a
 * decoded object, never from a localized `title`/`detail` the server sent.
 */

import { SITE_TYPE, WORK_CENTER_KIND } from './contract'
import type { CommandFailure, CommandUnknown } from './commandApi'

/**
 * `SiteType` enum names as they reach the wire. An UNKNOWN token is shown verbatim
 * rather than mapped to a guess or hidden: a site type the backend added is a fact
 * about the data, and inventing a Turkish label for it would be inventing meaning.
 */
const SITE_TYPE_LABEL: Record<string, string> = {
  [SITE_TYPE.factory]: 'Fabrika',
  [SITE_TYPE.warehouse]: 'Depo',
  [SITE_TYPE.externalWarehouse]: 'Harici depo',
}

export function siteTypeLabel(type: string): string {
  return SITE_TYPE_LABEL[type] ?? type
}

/**
 * `WorkCenterKind` tokens as they reach the wire. These are LABELS ONLY — the token is
 * never parsed for business meaning, and no capacity, routing or costing rule may be
 * derived from it here (that belongs to the backend calculator, not a screen).
 *
 * An unknown token is shown VERBATIM, exactly as `siteTypeLabel` does: a kind the
 * backend added is a fact about the data, and inventing a Turkish label for it would be
 * inventing meaning. Showing the raw token is also what makes the gap visible to whoever
 * must add the label.
 */
const WORK_CENTER_KIND_LABEL: Record<string, string> = {
  [WORK_CENTER_KIND.paint]: 'Boyama',
  [WORK_CENTER_KIND.tubeProfile]: 'Boru/Profil',
  [WORK_CENTER_KIND.slitting]: 'Dilme',
  [WORK_CENTER_KIND.coldRolling]: 'Soğuk haddeleme',
  [WORK_CENTER_KIND.cutToLength]: 'Boy kesme',
}

export function workCenterKindLabel(kind: string): string {
  return WORK_CENTER_KIND_LABEL[kind] ?? kind
}

export const WORK_CENTERS_HEADING = 'İş Merkezleri'
export const WORK_CENTERS_LEAD =
  'Bu şirket bağlamındaki iş merkezleri. Bir iş merkezinin bulunduğu lokasyonun kartını ' +
  'açmak için lokasyon bağlantısına tıklayın.'

/**
 * Shown in place of the link when the server says this caller may not open the row's
 * location card. It states the SCOPE, not a failure: the row itself is legitimately
 * visible, only its location card is out of reach.
 *
 * It must never read as an error and must never invite a retry — there is nothing to
 * retry. `canOpenLocationDetail` is the server's own answer, and the screen renders it
 * rather than probing the card to find out.
 */
export const LOCATION_NOT_OPENABLE = 'Lokasyon kartı bu yetkiyle açılamıyor'

/** ORG-04 — one work centre's read-only card. Only the record's own facts; nothing is claimed beyond them. */
export const WORK_CENTER_CARD_TEXT = {
  heading: 'İş merkezi',
  lead:
    'Bu iş merkezinin Organizasyon kaydı. Kapasite, vardiya ve üretim bilgisi bu kartın kaynağında yoktur; ' +
    'lokasyonun adı lokasyon kartındadır.',
  code: 'Kod',
  name: 'Ad',
  kind: 'Tür',
  status: 'Durum',
  location: 'Lokasyon',
  openLocation: 'Lokasyon kartını aç',
  openCard: 'İş merkezi kartını aç',
  subject: 'iş merkezi',
  contextSubject: 'iş merkezi kartı için tek şirket bağlamı',
} as const

export const ACTIVE_LABEL = 'Aktif'
export const PASSIVE_LABEL = 'Pasif'

export function activeStateLabel(isActive: boolean): string {
  return isActive ? ACTIVE_LABEL : PASSIVE_LABEL
}

export const INTERNAL_LABEL = 'Kendi lokasyonu'
export const EXTERNAL_LABEL = 'Harici'

export function externalLabel(isExternal: boolean): string {
  return isExternal ? EXTERNAL_LABEL : INTERNAL_LABEL
}

/**
 * Locations whose site is not in the `/sites` response.
 *
 * This is a REAL case, not defensive padding: `/sites` is scoped by
 * `organization.sites.read` and `/locations` by `organization.locations.read`, so a
 * user can hold location access whose facility they may not read. Such a row is
 * shown under an explicit heading — never dropped (that would hide a location the
 * user IS entitled to) and never filed under a guessed facility name.
 */
export const UNKNOWN_SITE_HEADING = 'Tesisi bu kapsamda görünmeyen lokasyonlar'

export const REASON_LABEL = 'Gerekçe'
export const REASON_MAX_LENGTH = 1000
export const REASON_REQUIRED =
  'Gerekçe zorunludur. Bu değişiklik gerekçesiyle birlikte audit kaydına yazılır.'
export const REASON_TOO_LONG = `Gerekçe en fazla ${REASON_MAX_LENGTH} karakter olabilir.`

/** Heading of the confirmation dialog, phrased as the action about to be taken. */
export function actionHeading(isActive: boolean): string {
  return isActive ? 'Lokasyonu pasife al' : 'Lokasyonu aktifleştir'
}

/** The action label, in the Business package's wording. */
export function confirmActionLabel(isActive: boolean): string {
  return isActive ? 'Pasife al' : 'Aktifleştir'
}

/** States the target explicitly, so the user confirms a fact and not a verb. */
export function targetStateSummary(code: string, isActive: boolean): string {
  return isActive
    ? `${code} lokasyonu AKTİF durumdan PASİF duruma alınacak.`
    : `${code} lokasyonu PASİF durumdan AKTİF duruma alınacak.`
}

export const CANCEL_LABEL = 'Vazgeç'

/** A definite refusal. Each kind gets its own text — never one generic error box. */
export function failureText(failure: CommandFailure): { title: string; detail: string } {
  switch (failure.kind) {
    case 'validation':
      return {
        title: 'İşlem kabul edilmedi',
        detail:
          'Sunucu bu isteği geçersiz buldu. Gerekçeyi gözden geçirip tekrar deneyin; ' +
          'değişiklik uygulanmadı.',
      }
    case 'unauthenticated':
      return {
        title: 'Oturum sonlandı',
        detail: 'Devam etmek için yeniden giriş yapın. Değişiklik uygulanmadı.',
      }
    case 'forbidden':
      return {
        title: 'Yetkiniz yok',
        detail: 'Bu lokasyonun durumunu değiştirme yetkiniz bulunmuyor. Değişiklik uygulanmadı.',
      }
    case 'not-found':
      return {
        title: 'Bulunamadı',
        detail: 'İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.',
      }
    case 'version-conflict':
      return {
        title: 'Kayıt bu sırada değişti',
        detail:
          'Lokasyon siz formu açtıktan sonra başka bir işlemle güncellendi; değişikliğiniz ' +
          'uygulanmadı. Güncel kaydı yükleyip kararınızı tekrar verin.',
      }
    case 'idempotency-conflict':
      return {
        title: 'Bu işlem anahtarı farklı bir istekle kullanılmış',
        detail:
          'Aynı anahtar başka bir içerikle daha önce kullanıldığı için istek reddedildi; ' +
          'bu değişiklik uygulanmadı. Güncel kaydı yükleyip yeni bir işlem başlatın.',
      }
    case 'unsupported-media':
      return {
        title: 'İstek biçimi desteklenmiyor',
        detail: 'İstek sunucunun beklediği biçimde değil. Değişiklik uygulanmadı.',
      }
    default:
      return {
        title: 'İstek biçimi desteklenmiyor',
        detail: 'İstek sunucunun beklediği biçimde değil. Değişiklik uygulanmadı.',
      }
  }
}

/**
 * Shown when a LATER request was turned away while an EARLIER one is still unresolved.
 *
 * The refusal is real and worth showing, but it is about that retry — not about the
 * first command, which may already be recorded. So this text deliberately never says
 * "uygulanmadı"; the surrounding unknown notice keeps owning that uncertainty.
 */
export function retryRefusedText(failure: CommandFailure): string {
  return `Son deneme sunucu tarafından reddedildi (${failureText(failure).title}). Bu, ilk isteğin uygulanmadığı anlamına gelmez; yukarıdaki belirsizlik sürüyor.`
}

/**
 * The version numbers on a 409.
 *
 * A REPLAYED 409 carries the conflict snapshot from when the command first ran, which
 * may be several versions stale (verified at PR #241 head `76c1be3`). So the second
 * number is only called "güncel" when the answer is not a replay; otherwise it is
 * named for what it actually is — the version seen at the time of the original
 * attempt — and the user is pointed at the fresh read instead.
 */
export function conflictVersionsText(
  expectedVersion: number,
  currentVersion: number,
  conflictIsCurrent: boolean,
): string {
  return conflictIsCurrent
    ? `Beklediğiniz sürüm ${expectedVersion}, kaydın güncel sürümü ${currentVersion}.`
    : `Bu yanıt daha önceki aynı işlemin tekrarı: o sırada beklenen sürüm ${expectedVersion}, ` +
      `görülen sürüm ${currentVersion} idi. Bu sayılar kaydın şu anki sürümü değildir; ` +
      'güncel durumu aşağıdaki taze görünümden okuyun.'
}

/**
 * An UNKNOWN outcome. The wording is deliberate and is the whole point of this
 * state: we must not say "uygulanmadı", because it may well have been applied.
 */
export function unknownText(reason: CommandUnknown): { title: string; detail: string } {
  const shared =
    'İstek sunucuya ulaşmış ve uygulanmış olabilir. Kendiliğinden tekrar gönderilmedi. ' +
    'Aynı işlem anahtarıyla yeniden gönderebilir veya güncel kaydı yükleyip durumu ' +
    'kontrol edebilirsiniz.'
  switch (reason.kind) {
    case 'offline':
      return { title: 'Bağlantı yok — sonuç bilinmiyor', detail: shared }
    case 'network-error':
      return { title: 'Sunucuya ulaşılamadı — sonuç bilinmiyor', detail: shared }
    case 'service-unavailable':
      return {
        title: `Servis yanıt veremedi (${reason.status}) — sonuç bilinmiyor`,
        detail: shared,
      }
    case 'unexpected-success':
      return {
        title: `Sözleşme dışı yanıt (${reason.status}) — sonuç bilinmiyor`,
        detail:
          `Sunucu bu işlem için tanımlı olmayan bir başarı kodu döndürdü (${reason.status}); ` +
          'yalnız 204 kabul edilir. ' +
          shared,
      }
    case 'undecodable':
      return {
        title: `Yanıt çözümlenemedi (${reason.status}) — sonuç bilinmiyor`,
        detail:
          'Sunucu yanıtı bu işlemin sözleşmesinde tanımlı değil veya okunamadı. ' + shared,
      }
    default:
      return { title: 'İşlem yarıda kesildi — sonuç bilinmiyor', detail: shared }
  }
}

/**
 * What we say after a 204.
 *
 * A 204 carries NO body and NO version, and a REPLAYED 204 is the server repeating an
 * earlier command's terminal answer — it is explicitly not a statement about the
 * record's state right now (a command that deactivated a location, replayed after
 * someone reactivated it, still answers 204). So neither message claims a resulting
 * state: both say the command was accepted and point at the fresh read, which is the
 * only thing entitled to say what the location IS.
 */
export const APPLIED_TITLE = 'Değişiklik kaydedildi'
export const APPLIED_DETAIL = 'Sunucu isteği kabul etti. Aşağıdaki kart güncel kayıttan yenilendi.'

export const REPLAYED_TITLE = 'Aynı işlem zaten kaydedilmişti'
export const REPLAYED_DETAIL =
  'Sunucu bu işlemi daha önce aldığı için tekrar uygulamadı ve önceki sonucunu döndürdü. ' +
  'Kaydın şu anki durumu aşağıdaki güncel görünümdedir.'

export const REFRESHING_DETAIL = 'Güncel kayıt yükleniyor…'

/** The site types a new site may take, in the order the form offers them (the wire tokens `SiteType` publishes). */
export const SITE_TYPE_OPTIONS: readonly string[] = [
  SITE_TYPE.factory,
  SITE_TYPE.warehouse,
  SITE_TYPE.externalWarehouse,
]

/** The work-centre kinds a new work centre may take (the wire tokens `WorkCenterKind` publishes). */
export const WORK_CENTER_KIND_OPTIONS: readonly string[] = [
  WORK_CENTER_KIND.paint,
  WORK_CENTER_KIND.tubeProfile,
  WORK_CENTER_KIND.slitting,
  WORK_CENTER_KIND.coldRolling,
  WORK_CENTER_KIND.cutToLength,
]

/**
 * The master edit (create and edit sites, locations and work centres). Every message is chosen from a status code
 * or a decoded object; none claims a resulting state — after a save the card is re-read and says what the record is.
 */
export const MASTER_TEXT = {
  code: 'Kod',
  name: 'Ad',
  reason: 'Gerekçe',
  siteType: 'Tesis türü',
  site: 'Tesis',
  location: 'Lokasyon',
  kind: 'İş merkezi türü',
  choose: 'Seçin',
  save: 'Kaydet',
  cancel: 'Vazgeç',
  resend: 'Aynı işlem anahtarıyla tekrar gönder',
  reload: 'Güncel kaydı yükle',
  codeMax: 64,
  nameMax: 200,
  codeRequired: 'Kod zorunludur (en fazla 64 karakter).',
  nameRequired: 'Ad zorunludur (en fazla 200 karakter).',
  parentRequired: 'Bağlanacağı kayıt seçilmelidir.',
  typeRequired: 'Tür seçilmelidir.',
  help: 'Kaydedilen değişiklik gerekçesiyle birlikte audit kaydına yazılır.',
  frozenHelp:
    'Sonucu bilinmeyen bir kayıt işlemi var. Çözülene kadar alanlar değiştirilemez; aynı işlem anahtarıyla ' +
    'tekrar gönderebilir veya vazgeçip güncel kaydı kontrol edebilirsiniz.',
  fixedFacts:
    'Tür ve bağlı olduğu kayıt oluşturulurken belirlenir; burada yalnız kod ve ad değiştirilir.',
  externalFromSite:
    'Lokasyonun harici olup olmadığı seçilen tesisin türünden gelir: harici depo tesisindeki lokasyon haricidir.',
  kindFixed: 'İş merkezi türü üretim planlamasının referansıdır; oluşturulduktan sonra değiştirilemez.',
  savedTitle: 'Değişiklik kaydedildi',
  savedDetail: 'Sunucu isteği kabul etti. Kart güncel kayıttan yenilendi.',
  createdTitle: 'Kayıt oluşturuldu',
  createdDetail: 'Sunucu kaydı oluşturdu. Liste güncel kayıttan yenilendi.',
  replayedTitle: 'Aynı işlem zaten kaydedilmişti',
  replayedDetail:
    'Sunucu bu işlemi daha önce aldığı için tekrar uygulamadı ve önceki sonucunu döndürdü. Kaydın şu anki ' +
    'durumu güncel görünümdedir.',
  codeInUseTitle: 'Kod kullanımda',
  codeInUseDetail: 'Bu kod şirketteki başka bir kayıtta kullanılıyor. Farklı bir kod girin; kayıt yapılmadı.',
  newSite: 'Yeni tesis',
  editSite: 'Tesisi düzenle',
  newLocation: 'Yeni lokasyon',
  editLocation: 'Lokasyonu düzenle',
  newWorkCenter: 'Yeni iş merkezi',
  editWorkCenter: 'İş merkezini düzenle',
  locationsLoading: 'Lokasyonlar yükleniyor…',
  locationsUnavailable: 'Lokasyon listesi okunamadı; iş merkezi için lokasyon seçilemiyor.',
  locationsTooMany: 'Lokasyon listesi bu formun sınırını aşıyor; seçim listesi eksik olacağı için sunulmuyor.',
  sitesUnavailable: 'Tesis listesi okunamadı; lokasyon için tesis seçilemiyor.',
  conflictReloadedTitle: 'Güncel kayıt yüklendi',
  conflictReloadedDetail:
    'Girdiğiniz değerler korundu. Güncel kayıttan farklı olan alanlar aşağıda; kaydederseniz değişikliğiniz ' +
    'güncel sürüm üzerinden yeni bir işlem olarak gönderilir.',
  conflictReloadedSame:
    'Girdiğiniz kod ve ad güncel kayıtla aynı. Gerekçeyi kontrol edip yeniden kaydedebilirsiniz.',
  conflictDiffLabel: 'Girdiğiniz değerler ile güncel kayıt',
  conflictDiffField: 'Alan',
  conflictDiffMine: 'Sizin girdiniz',
  conflictDiffCurrent: 'Güncel kayıt',
  conflictEmpty: '(boş)',
} as const

/** The notice after an accepted edit (204): accepted, or an earlier identical command replayed — never a state. */
export function savedNotice(replayed: boolean): { title: string; detail: string; outcome: string } {
  return replayed
    ? { title: MASTER_TEXT.replayedTitle, detail: MASTER_TEXT.replayedDetail, outcome: 'replayed' }
    : { title: MASTER_TEXT.savedTitle, detail: MASTER_TEXT.savedDetail, outcome: 'saved' }
}

/** The notice after a create (201) on a list screen. The list, re-read, shows what was stored. */
export function createdNotice(replayed: boolean): { title: string; detail: string; outcome: string } {
  return replayed
    ? { title: MASTER_TEXT.replayedTitle, detail: MASTER_TEXT.replayedDetail, outcome: 'replayed' }
    : { title: MASTER_TEXT.createdTitle, detail: MASTER_TEXT.createdDetail, outcome: 'created' }
}

/**
 * A later retry of a master-data command was refused while the first one is still unresolved (BLG-11).
 * Like `retryRefusedText`, it never says the first request was not applied.
 */
export function masterRetryRefusedText(failure: CommandFailure | { readonly kind: 'code-in-use' }): string {
  return `Son deneme sunucu tarafından reddedildi (${masterFailureText(failure).title}). Bu, ilk isteğin uygulanmadığı anlamına gelmez.`
}

/** A definite refusal of a master-data command. Each kind has its own text; none says the record is changed. */
export function masterFailureText(
  failure: CommandFailure | { readonly kind: 'code-in-use' },
): { title: string; detail: string } {
  switch (failure.kind) {
    case 'code-in-use':
      return { title: MASTER_TEXT.codeInUseTitle, detail: MASTER_TEXT.codeInUseDetail }
    case 'forbidden':
      return { title: 'Yetkiniz yok', detail: 'Bu kaydı oluşturma veya düzenleme yetkiniz bulunmuyor. Kayıt yapılmadı.' }
    case 'not-found':
      return {
        title: 'Bulunamadı',
        detail: 'Kayıt veya bağlanacağı kayıt bulunamadı ya da bu bağlamda kullanılamıyor. Kayıt yapılmadı.',
      }
    case 'version-conflict':
      return {
        title: 'Kayıt bu sırada değişti',
        detail:
          'Kayıt siz formu açtıktan sonra başka bir işlemle güncellendi; değişikliğiniz uygulanmadı. Güncel ' +
          'kaydı yükleyip tekrar düzenleyin.',
      }
    case 'validation':
      return {
        title: 'İşlem kabul edilmedi',
        detail: 'Sunucu alanları geçersiz buldu. Kod, ad ve gerekçeyi gözden geçirin; kayıt yapılmadı.',
      }
    default:
      return failureText(failure)
  }
}

/** O02 — the operator of an external location (who runs it). Party codes and names are shown as the server sent them. */
export const OPERATOR_TEXT = {
  heading: 'İşletici',
  lead: 'Bu harici lokasyonu işleten cari. Atama gerekçeyle kaydedilir ve denetim kaydına yazılır.',
  subject: 'lokasyon işleticisi',
  none: 'Atanmış işletici yok.',
  code: 'Cari kodu',
  legalName: 'Unvan',
  tradeName: 'Ticari ad',
  ineligible: 'Şu an işletici olarak uygun değil',
  ineligibleDetail:
    'Kayıtlı işletici sunucunun güncel işletici koşullarını artık sağlamıyor. Kayıt değişmedi; yeni bir atama ' +
    'gerekebilir.',
  assign: 'İşletici ata',
  change: 'İşleticiyi değiştir',
  formHeading: 'İşletici seç',
  searchFormLabel: 'İşletici adayı arama',
  searchLabel: 'Cari ara',
  searchHelp: 'Kod, unvan veya ticari ad. Yalnız işletici olabilecek cariler listelenir.',
  searchTooLong: (max: number) => `Arama metni en fazla ${max} karakter olabilir.`,
  search: 'Ara',
  candidatesLegend: 'Aday cariler',
  candidatesSubject: 'işletici adayları',
  noCandidates: 'Bu aramayla işletici olabilecek cari bulunamadı.',
  nextPage: 'Sonraki adaylar',
  firstPage: 'İlk adaylara dön',
  invalidCursor: 'Aday listesinin sayfa bilgisi artık geçerli değil. Listeyi baştan açın.',
  current: '(mevcut işletici)',
  pickRequired: 'Bir cari seçin.',
  save: 'İşleticiyi kaydet',
  resend: 'Aynı işlemle tekrar gönder',
  reload: 'Güncel kaydı yükle',
  ineligibleTitle: 'Bu cari işletici olarak atanamıyor',
  ineligibleRefusal:
    'Sunucu bu cariyi bu lokasyon için uygun bulmadı (lokasyon harici değil veya cari işletici koşullarını ' +
    'sağlamıyor). Atama yapılmadı.',
  dependencyTitle: 'Cari doğrulaması şu an yapılamıyor',
  dependencyDetail:
    'Sunucu cari uygunluğunu kontrol edemedi ve hiçbir şey kaydetmedi. Aynı işlemle tekrar gönderebilirsiniz.',
  forbiddenDetail: 'Bu lokasyonun işleticisini değiştirme yetkiniz bulunmuyor. Atama yapılmadı.',
} as const

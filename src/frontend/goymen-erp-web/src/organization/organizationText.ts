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
 * `WorkCenterKind` enum names as they reach the wire. An UNKNOWN token is shown
 * verbatim for the reason `siteTypeLabel` gives: a kind the backend added is a fact
 * about the data, and inventing a Turkish label for it would be inventing meaning.
 */
const WORK_CENTER_KIND_LABEL: Record<string, string> = {
  [WORK_CENTER_KIND.paint]: 'Boyama',
  [WORK_CENTER_KIND.tubeProfile]: 'Boru / Profil',
  [WORK_CENTER_KIND.slitting]: 'Dilme',
  [WORK_CENTER_KIND.coldRolling]: 'Soğuk hadde',
  [WORK_CENTER_KIND.cutToLength]: 'Boy kesme',
}

export function workCenterKindLabel(kind: string): string {
  return WORK_CENTER_KIND_LABEL[kind] ?? kind
}

export const WORK_CENTER_HEADING = 'İş Merkezleri'

export const WORK_CENTER_LEAD =
  'Bu şirket bağlamında yetkiniz dâhilindeki iş merkezleri. Bir iş merkezinin ' +
  'lokasyonunu açmak için satırdaki lokasyon bağlantısını kullanın.'

/**
 * The count line. It names its own scope for the reason the location summary does: the
 * server already scoped the rows to this caller, and a bare number would read as the
 * company's total.
 */
export function workCenterCountText(count: number): string {
  return `Yetkiniz dâhilindeki ${count} iş merkezi gösteriliyor.`
}

/**
 * Shown in the location column when the caller does not hold the location-card
 * capability.
 *
 * It is a SCOPE STATEMENT, not a failure and not a disclosure: no request was made, so
 * we know nothing about that location beyond the id the work-center row already
 * carried, and this text claims nothing more. Rendering a dead link or a link that only
 * 404s at the server would both be worse — one lies about what is available, the other
 * spends a refusal to say what the capability already said.
 */
export const WORK_CENTER_LOCATION_UNAVAILABLE = 'Lokasyon kartı kapsam dışı'

/** Accessible name for a row's location link — the id is never shown as a label. */
export function workCenterLocationLinkLabel(code: string): string {
  return `${code} iş merkezinin lokasyonunu aç`
}

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

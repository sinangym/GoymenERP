/**
 * ORG-05 (KIM-S23) — every user-facing text of the company VKN screen, in one localizable file.
 *
 * The texts state what the server decides and never promise more: the screen never says a VKN "changed" until a
 * fresh read shows it, and an unknown outcome never says "uygulanmadı".
 */

import type { CompanyTaxIdFailure } from './companyTaxIdApi'
import type { TaxIdChangeStatus } from './companyTaxIdContract'
import { COMPANY_TAX_ID_LIMITS } from './companyTaxIdContract'
import { failureText } from './organizationText'

export const COMPANY_TAX_ID_TEXT = {
  heading: 'Şirket Vergi Numarası',
  lead:
    'Etkin şirketin vergi kimlik numarası (VKN). VKN değişikliği iki kişiyle yapılır: biri talebi hazırlar, ' +
    'başka biri onaylar veya reddeder. Onaylanana kadar şirketin VKN’si değişmez; her adım denetim kaydına yazılır.',
  subject: 'şirket vergi numarası',
  contextSubject: 'şirket vergi numarası (tek şirket bağlamı gerekir)',
  code: 'Şirket kodu',
  legalName: 'Ticari unvan',
  vkn: 'VKN',
  noVkn: 'Henüz VKN girilmemiş (geçici şirket kaydı)',
  placeholderNote:
    'Bu şirket kaydı geçici olarak oluşturulmuş. İlk VKN de aynı talep ve onay akışıyla girilir.',

  requestHeading: 'VKN değişikliği talep et',
  requestHelp:
    'Talep gönderildiğinde şirketin VKN’si değişmez; başka bir yetkili onaylayana kadar bekleyen talep olarak kalır.',
  proposedVkn: 'Yeni VKN',
  proposedVknHelp: '10 rakam',
  reason: 'Gerekçe',
  submitRequest: 'Talebi gönder',
  cancel: 'Vazgeç',
  openRequest: 'VKN değişikliği talep et',
  vknInvalid: 'VKN tam olarak 10 rakamdan oluşmalıdır.',
  vknSame: 'Yeni VKN mevcut VKN ile aynı olamaz.',
  reasonRequired: 'Gerekçe zorunludur.',
  reasonTooLong: `Gerekçe en fazla ${COMPANY_TAX_ID_LIMITS.reason} karakter olabilir.`,
  noteTooLong: `Karar notu en fazla ${COMPANY_TAX_ID_LIMITS.note} karakter olabilir.`,
  requestNotAllowedWhilePending:
    'Bekleyen bir VKN değişiklik talebi var. Yeni talep, mevcut talep karara bağlandıktan sonra hazırlanabilir.',

  pendingHeading: 'Bekleyen VKN değişiklik talebi',
  lastDecidedHeading: 'Son karara bağlanan talep',
  currentVkn: 'Mevcut VKN',
  requestedBy: 'Hazırlayan',
  requestedAt: 'Hazırlanma zamanı',
  decidedBy: 'Karar veren',
  decidedAt: 'Karar zamanı',
  decisionNote: 'Karar notu',
  status: 'Durum',
  none: '—',

  decisionHeading: 'Talebi karara bağla',
  decisionHelp:
    'Onay, önerilen VKN’yi şirkete uygular. Şirket kaydı talep hazırlandıktan sonra değiştiyse talep yalnız reddedilebilir.',
  note: 'Karar notu (isteğe bağlı)',
  approve: 'Onayla',
  reject: 'Reddet',
  ownRequest:
    'Bu talebi siz hazırladınız. Hazırlayan kişi kendi talebini onaylayamaz veya reddedemez; başka bir yetkili karar vermelidir.',
  awaitingApprover: 'Talep, onay yetkisi olan başka bir kişinin kararını bekliyor.',

  resend: 'Aynı işlem anahtarıyla tekrar gönder',
  reload: 'Güncel kaydı yükle',
  frozenHelp:
    'Sonucu bilinmeyen bir işlem var. Çözülene kadar alanlar değiştirilemez; aynı işlem anahtarıyla tekrar ' +
    'gönderebilir veya güncel kaydı yükleyip durumu kontrol edebilirsiniz.',

  requestedTitle: 'Talep kaydedildi',
  requestedDetail: 'VKN değişiklik talebi bekleyen talep olarak kaydedildi. Şirketin VKN’si onaylanana kadar değişmez.',
  decidedTitle: 'Karar kaydedildi',
  decidedDetail: 'Karar kaydedildi. Aşağıdaki görünüm güncel kayıttan yenilendi.',
  replayedTitle: 'Aynı işlem zaten kaydedilmişti',
  replayedDetail:
    'Sunucu bu işlemi daha önce aldığı için tekrar uygulamadı ve önceki sonucunu döndürdü. Güncel durum aşağıdaki ' +
    'görünümdedir.',
} as const

export function taxIdStatusLabel(status: TaxIdChangeStatus): string {
  switch (status) {
    case 'pending':
      return 'Onay bekliyor'
    case 'approved':
      return 'Onaylandı'
    case 'rejected':
      return 'Reddedildi'
    default:
      return status
  }
}

/** A definite refusal of a tax-id command. Each kind gets its own text — never one generic error box. */
export function taxIdFailureText(failure: CompanyTaxIdFailure): { title: string; detail: string } {
  switch (failure.kind) {
    case 'tax-id-in-use':
      return {
        title: 'VKN başka bir şirkette kayıtlı',
        detail: 'Aynı VKN iki şirkette olamaz. Önerilen VKN’yi kontrol edin; işlem uygulanmadı.',
      }
    case 'tax-id-check-digit-invalid':
      return {
        title: 'VKN kontrol hanesi hatalı',
        detail:
          'Önerilen VKN resmî kontrol hanesi kuralını sağlamıyor. VKN’yi belgeden kontrol edip yeniden girin; ' +
          'talep oluşturulmadı.',
      }
    case 'tax-id-change-pending':
      return {
        title: 'Bekleyen bir talep var',
        detail:
          'Bu şirket için karara bağlanmamış bir VKN değişiklik talebi zaten var. Yeni talep uygulanmadı; ' +
          'güncel kaydı yükleyin.',
      }
    case 'tax-id-unchanged':
      return {
        title: 'VKN aynı',
        detail: 'Önerilen VKN şirketin mevcut VKN’si ile aynı. Talep uygulanmadı.',
      }
    case 'tax-id-change-not-pending':
      return {
        title: 'Talep artık beklemiyor',
        detail: 'Bu talep başka bir işlemle karara bağlanmış. Kararınız uygulanmadı; güncel kaydı yükleyin.',
      }
    case 'tax-id-change-stale':
      return {
        title: 'Şirket kaydı talepten sonra değişti',
        detail:
          'Talep hazırlandıktan sonra şirket kaydı değiştiği için onay uygulanamaz. Talep yalnız reddedilebilir; ' +
          'gerekirse yeni bir talep hazırlanmalıdır.',
      }
    case 'tax-id-self-decision':
      return {
        title: 'Kendi talebinize karar veremezsiniz',
        detail: 'Talebi hazırlayan kişi onu onaylayamaz veya reddedemez. Kararınız uygulanmadı.',
      }
    case 'forbidden':
      return {
        title: 'Yetkiniz yok',
        detail: 'Bu VKN işlemi için yetkiniz bulunmuyor. İşlem uygulanmadı.',
      }
    case 'version-conflict':
      return {
        title: 'Kayıt bu sırada değişti',
        detail:
          'Şirket kaydı siz ekranı açtıktan sonra güncellendi; işleminiz uygulanmadı. Güncel kaydı yükleyip tekrar deneyin.',
      }
    default:
      return failureText(failure)
  }
}

/** The notice after an accepted command: accepted, or an earlier identical command replayed — never a state. */
export function taxIdDoneNotice(
  kind: 'requested' | 'decided',
  replayed: boolean,
): { title: string; detail: string; outcome: string } {
  if (replayed) {
    return {
      title: COMPANY_TAX_ID_TEXT.replayedTitle,
      detail: COMPANY_TAX_ID_TEXT.replayedDetail,
      outcome: 'replayed',
    }
  }
  return kind === 'requested'
    ? { title: COMPANY_TAX_ID_TEXT.requestedTitle, detail: COMPANY_TAX_ID_TEXT.requestedDetail, outcome: 'requested' }
    : { title: COMPANY_TAX_ID_TEXT.decidedTitle, detail: COMPANY_TAX_ID_TEXT.decidedDetail, outcome: 'decided' }
}

/** Shown when a resend was turned away while the first attempt stays unresolved; never says "uygulanmadı". */
export function taxIdRetryRefusedText(failure: CompanyTaxIdFailure): string {
  return `Son deneme sunucu tarafından reddedildi (${taxIdFailureText(failure).title}). Bu, ilk isteğin uygulanmadığı anlamına gelmez; yukarıdaki belirsizlik sürüyor.`
}

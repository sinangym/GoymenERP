/**
 * Turkish texts of the discount limit screen (SAT-D61…D70). Every user-facing string lives here; the screen composes
 * nothing from a server's localized title or detail.
 */

import { LIMIT_INPUT, LIMIT_PROBLEM } from './discountLimitContract'

export const LIMIT_TEXT = {
  title: 'İskonto limitleri',
  intro:
    'Satış iskontosunun onaysız sınırı limit gruplarıyla tanımlanır. Gruplar rollerden ayrıdır; kişiler gruplara bağlanır. ' +
    'Birden fazla grubu olan kişide en yüksek limit geçerlidir; limiti olmayan kişide her iskonto onaya gider.',
  contextSubject: 'iskonto limitleri',
  groupsHeading: 'Limit grupları',
  groupsSubject: 'limit grupları',
  groupsTableLabel: 'Limit grupları ve limitleri',
  noGroups: 'Bu şirkette henüz limit grubu yok.',
  newGroup: 'Yeni limit grubu',
  columnCode: 'Kod',
  columnName: 'Ad',
  columnStatus: 'Durum',
  columnRecorded: 'Kayıtlı limit',
  columnEffective: 'Bugün geçerli limit',
  columnActions: 'İşlemler',
  active: 'Etkin',
  passive: 'Pasif',
  noLimitRecorded: 'Kayıt yok',
  noLimit: 'Limit yok — her iskonto onaya gider',
  editLimit: 'Limiti düzenle',
  rename: 'Adı değiştir',
  deactivate: 'Pasife al',
  codeLabel: 'Kod',
  codeHelp: `Büyük harf, rakam, "_" ve "-"; en çok ${LIMIT_INPUT.codeMaximumLength} karakter. Kişi bağı şirketler arasında bu kodla eşleşir; sonradan değişmez.`,
  nameLabel: 'Ad',
  reasonLabel: 'Gerekçe',
  reasonHelp: `Zorunlu; en çok ${LIMIT_INPUT.reasonMaximumLength} karakter. Değişiklik gerekçesiyle birlikte kayda geçer.`,
  kindLegend: 'Limit biçimi',
  kindFixed: 'Sabit yüzde',
  kindLinked: 'Başka grubun limiti + ek puan',
  percentLabel: 'Yüzde (0-100)',
  linkedGroupLabel: 'Bağlı grup',
  linkedGroupPlaceholder: 'Grup seçin',
  extraPointsLabel: 'Ek yüzde puan (0-100)',
  limitHelp:
    'Değişiklik bugünden geçerli olur; daha önce kontrol edilmiş siparişlerin kaydı değişmez. Bağlı limit, bağlı grubun limitine ek puan eklenerek ' +
    'hesaplanır ve %100 ile sınırlıdır; bağlı grubun limiti yoksa iskonto onaya gider.',
  deactivateConfirm: 'Pasif grup limite katkı vermez ve yeniden değiştirilemez. Pasife almak istiyor musunuz?',
  save: 'Kaydet',
  cancel: 'Vazgeç',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  saved: 'Değişiklik kaydedildi.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; sonuç yeniden gösterildi.',
  unknownOutcome:
    'Sonuç alınamadı; işlem uygulanmış olabilir. "Aynı işlemi yeniden dene" aynı işlem anahtarıyla gönderir ve iki kez uygulanmaz.',
  bindingHeading: 'Kişi → limit grubu bağı',
  bindingIntro:
    'Bağ, işlem yapabildiğiniz tüm şirketlere birlikte yazılır. Bir şirkette kişi üye değilse, grup yoksa veya grup pasifse hiçbir şirkete yazılmaz.',
  searchLabel: 'Kişi ara',
  searchHelp: 'Ad içinden arayın (en çok 25 sonuç).',
  search: 'Ara',
  peopleSubject: 'kişiler',
  noPeople: 'Aramaya uyan etkin kişi yok.',
  morePeople: 'Daha fazla sonuç var; aramayı daraltın.',
  peopleListLabel: 'Arama sonuçları',
  selectPerson: 'Seç',
  selectedPerson: 'Seçili kişi',
  bindingSubject: 'kişinin grupları',
  bindingTableLabel: 'Şirketlere göre mevcut bağ',
  columnCompany: 'Şirket',
  columnGroups: 'Gruplar',
  noBinding: 'Bağ yok',
  groupsLegend: 'Bağlanacak gruplar (bu şirketin etkin grupları)',
  noActiveGroups: 'Bağlanabilecek etkin grup yok.',
  saveBinding: 'Tüm şirketlere kaydet',
  reload: 'Güncel hâli yükle',
} as const

/** A refusal's text by its stable code (or status when the body is code-less). */
export function refusalText(status: number, code: string | null, companyLabel: string | null, groupCode: string | null): string {
  const where = companyLabel === null ? '' : ` (${companyLabel})`
  switch (code) {
    case LIMIT_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case LIMIT_PROBLEM.reasonTooLong:
      return `Gerekçe en fazla ${LIMIT_INPUT.reasonMaximumLength} karakter olabilir.`
    case LIMIT_PROBLEM.requestInvalid:
      return 'Girilen değerler geçersiz; alanları kontrol edin.'
    case LIMIT_PROBLEM.versionConflict:
      return `Kayıt siz açtıktan sonra değişti${where}. Güncel hâli yüklendi; değişikliği yeniden yapın.`
    case LIMIT_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açıp tekrar deneyin.'
    case LIMIT_PROBLEM.groupCodeTaken:
      return 'Bu şirkette aynı kodlu bir limit grubu var.'
    case LIMIT_PROBLEM.groupNameTaken:
      return 'Bu şirkette aynı adlı bir limit grubu var.'
    case LIMIT_PROBLEM.groupInactive:
      return 'Pasif limit grubu değiştirilemez.'
    case LIMIT_PROBLEM.linkedGroupNotFound:
      return 'Bağlanan grup bu şirkette yok.'
    case LIMIT_PROBLEM.linkedGroupInactive:
      return 'Pasif bir gruba bağlanılamaz.'
    case LIMIT_PROBLEM.linkCycle:
      return 'Bu bağlantı gruplar arasında döngü oluşturur (örn. A→B→A); kaydedilmedi.'
    case LIMIT_PROBLEM.bindingCompaniesMismatch:
      return 'Şirket listeniz değişmiş; güncel hâli yüklendi, yeniden kaydedin.'
    case LIMIT_PROBLEM.bindingPersonNotMember:
      return `Kişi bu şirkette etkin üye değil${where}; bağ hiçbir şirkete yazılmadı.`
    case LIMIT_PROBLEM.bindingGroupNotFound:
      return `"${groupCode ?? '?'}" kodlu grup bu şirkette yok${where}; bağ hiçbir şirkete yazılmadı.`
    case LIMIT_PROBLEM.bindingGroupInactive:
      return `"${groupCode ?? '?'}" kodlu grup bu şirkette pasif${where}; bağ hiçbir şirkete yazılmadı.`
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok (işlem yapabildiğiniz her şirkette gerekir).'
      if (status === 404) return 'Grup bu şirkette bulunamadı.'
      return 'İşlem kabul edilmedi.'
  }
}

/** A limit as stored: "Sabit %35" or "SATIS-DESTEK + 3 puan". */
export function definitionLabel(
  definition: { readonly kind: string; readonly percent: string | null; readonly extraPoints: string | null },
  linkedLabel: string | null,
): string {
  return definition.kind === 'Fixed'
    ? `Sabit %${definition.percent ?? '?'}`
    : `${linkedLabel ?? 'Bilinmeyen grup'} + ${definition.extraPoints ?? '?'} puan`
}

export function percentLabel(percent: string): string {
  return `%${percent}`
}

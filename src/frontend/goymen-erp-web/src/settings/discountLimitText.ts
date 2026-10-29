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
  columnRecorded: 'Bugünkü kayıt',
  columnEffective: 'Bugün geçerli limit',
  columnScheduled: 'Planlı değişiklikler',
  noScheduled: 'Yok',
  columnActions: 'İşlemler',
  active: 'Etkin',
  passive: 'Pasif',
  noLimitRecorded: 'Kayıt yok',
  noLimit: 'Limit yok — her iskonto onaya gider',
  invalidLimit: 'Kayıtlar tutarsız — satış bu grubun limitini okuyamaz, iskontolu satır kaydedilemez',
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
    'Değişiklik seçilen tarihten itibaren kendiliğinden geçerli olur (bugün veya ileri bir tarih); daha önce kontrol edilmiş siparişlerin kaydı değişmez. ' +
    'Bağlı limit, bağlı grubun o tarihteki limitine ek puan eklenerek hesaplanır ve %100 ile sınırlıdır; bağlı grubun limiti yoksa iskonto onaya gider.',
  effectiveFromLabel: 'Geçerlilik başlangıcı',
  effectiveFromHelp:
    'Bugün veya ileri bir tarih. Planlı bir değişiklik varsa yeni kayıt o tarihten önce başlayamaz; aynı tarihi seçerseniz yeni kayıt planlı kaydın yerine geçer (planlı kayıt geri çekilir).',
  effectiveFromError: 'Tarih bugünden ve son planlı değişiklikten önce olamaz.',
  scheduledListLabel: 'Planlı değişiklikler',
  correctScheduled: 'Düzelt',
  withdrawScheduled: 'Geri çek',
  correctHeading: 'Planlı limiti düzelt',
  withdrawHeading: 'Planlı limiti geri çek',
  withdrawConfirm:
    'Planlı kayıt yürürlüğe girmeden geri çekilir; o tarihte, varsa ondan önceki planlı kayıt, yoksa bugün geçerli limit geçerli kalır. Daha önce yerine geçilmiş kayıtlar geri gelmez. Yürürlüğe girmiş kayıt geri çekilemez.',
  correctHelp:
    'Planlı kayıt geri çekilir ve yerine bu kayıt yazılır (tek işlem). Tarih, düzeltilen kaydın tarihiyle gelir; değiştirebilirsiniz. Yalnız en son planlı kayıt düzeltilebilir; yürürlüğe girmiş kayıt düzeltilemez, yeni kayıt girilir.',
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
    'Bağ, işlem yapabildiğiniz şirketlerden kişinin çalıştığı şirketlerin tamamına birlikte yazılır; çalışmadığı şirketler atlanır ve aşağıda gösterilir. Kişi sonradan başka bir şirkete katılırsa bağ kendiliğinden genişlemez; yeniden kaydedin. Çalıştığı bir şirkette yetkiniz yoksa, grup yoksa veya grup pasifse hiçbir şirkete yazılmaz.',
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
  skippedCompany: 'Atlanır — kişi bu şirkette çalışmıyor',
  memberOfNone: 'Kişi, işlem yapabildiğiniz şirketlerin hiçbirinde çalışmıyor; bağ yazılamaz.',
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
      return 'Bu bağlantı gruplar arasında döngü oluşturur (örn. A→B→A), bugün ya da planlı bir değişiklikten sonra; kaydedilmedi.'
    case LIMIT_PROBLEM.effectiveFromPast:
      return 'Geçerlilik başlangıcı bugünden önce olamaz.'
    case LIMIT_PROBLEM.recordInEffect:
      return 'Bu limit kaydı yürürlüğe girdi; değiştirilemez veya geri çekilemez. Yeni bir limit kaydı girin.'
    case LIMIT_PROBLEM.recordWithdrawn:
      return 'Bu limit kaydı daha önce geri çekildi.'
    case LIMIT_PROBLEM.effectiveFromBeforeScheduled:
      return 'Bu grubun daha ileri tarihli planlı bir limiti var; kayıt o tarihten önce başlayamaz. O tarihte geçerli olacak değeri değiştirmek için en son planlı kaydı düzeltin veya geri çekin.'
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

/** A business date (`yyyy-MM-dd`) as shown on screen: `dd.MM.yyyy`. Presentation only. */
export function businessDateLabel(value: string): string {
  const [year, month, day] = value.split('-')
  return `${day}.${month}.${year}`
}

/** SAT-D78: one scheduled change, e.g. "01.01.2027 itibarıyla: Sabit %35 — o gün %35". */
export function scheduledLabel(effectiveFrom: string, definition: string, effectivePercent: string | null): string {
  const effective = effectivePercent === null ? 'o gün limit yok' : `o gün %${effectivePercent}`
  return `${businessDateLabel(effectiveFrom)} itibarıyla: ${definition} — ${effective}`
}

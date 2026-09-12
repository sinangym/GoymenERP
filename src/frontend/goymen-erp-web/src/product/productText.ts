/**
 * Turkish user-facing text for the Product screens.
 *
 * AGENTS.md: code and technical names are English, user text is Turkish and lives
 * in a source file that can be localized. Kept in ONE module so the list, the 360
 * and the create form cannot drift into calling the same fact different things —
 * the Cari slice learned that the hard way with its role labels.
 */

import {
  COMPLETENESS_AREA,
  type Completeness,
  type CompletenessArea,
  type DetailSection,
  type MatchReason,
  type ProductLifecycle,
  type ProductRoleName,
  type ProductRoles,
  type RoleProvenance,
  type UnitRoleName,
} from './contract'

export const LIFECYCLE_LABEL: Record<ProductLifecycle, string> = {
  Draft: 'Taslak',
  Active: 'Aktif',
  Inactive: 'Pasif',
}

/**
 * What each lifecycle state MEANS for the operator, not just what it is called.
 *
 * "Taslak" on its own does not tell someone that the product will not appear in an
 * order line; DEC-PRD-010 and §6.1 make that the whole point of the state.
 */
export const LIFECYCLE_MEANING: Record<ProductLifecycle, string> = {
  Draft: 'Kesin işlemlerde seçilemez.',
  Active: 'Yeni işlemlerde seçilebilir.',
  Inactive: 'Yeni kullanıma kapalı; geçmiş kayıtlarda görünür.',
}

export const ROLE_LABEL: Record<ProductRoleName, string> = {
  purchasable: 'Alınabilir',
  sellable: 'Satılabilir',
  productionInput: 'Üretim girdisi',
  productionOutput: 'Üretim çıktısı',
}

export const ROLE_PROVENANCE_LABEL: Record<RoleProvenance, string> = {
  manual: 'elle girildi',
  derived: 'ilişkiden türetildi',
  'not-evaluated': 'değerlendirilmedi',
}

/**
 * How a row's roles read in one line.
 *
 * Three outcomes, deliberately distinct:
 *  - at least one asserted role → those roles;
 *  - every role determined and none true → an explicit "no role" sentence;
 *  - some role still `not-evaluated` → said out loud.
 *
 * The third case is why this function exists. §5.2 forbids persisting an
 * unevaluated role as `false`, and printing "Rol yok" for it would undo that rule
 * at the last step — the operator would read a determined answer the server never
 * gave.
 */
export const NO_ROLE_TEXT = 'Ticari/operasyonel rol yok'
export const ROLE_UNEVALUATED_SUFFIX = 'değerlendirilmedi'

export function rolesText(roles: ProductRoles): string {
  const asserted = (Object.keys(ROLE_LABEL) as ProductRoleName[]).filter(
    (name) => roles[name].value === true,
  )
  const pending = (Object.keys(ROLE_LABEL) as ProductRoleName[]).filter(
    (name) => roles[name].provenance === 'not-evaluated',
  )
  const parts: string[] = []
  if (asserted.length > 0) parts.push(asserted.map((name) => ROLE_LABEL[name]).join(', '))
  else if (pending.length === 0) parts.push(NO_ROLE_TEXT)
  if (pending.length > 0) {
    parts.push(
      `${pending.map((name) => ROLE_LABEL[name]).join(', ')} — ${ROLE_UNEVALUATED_SUFFIX}`,
    )
  }
  return parts.join(' · ')
}

export const COMPLETENESS_AREA_LABEL: Record<CompletenessArea, string> = {
  template: 'Şablon',
  unit: 'Birim',
  package: 'Paket',
}

export const COMPLETENESS_LABEL: Record<Completeness, string> = {
  complete: 'tam',
  incomplete: 'eksik',
  unknown: 'bilinmiyor',
}

/**
 * The areas that are actually incomplete, for the list's warning badge.
 *
 * `unknown` is NOT counted as incomplete: §9 forbids fabricating a value for data
 * the server does not support, and a warning badge is a claim that something is
 * missing. Unknown areas are surfaced separately on the 360.
 */
export function incompleteAreas(
  completeness: Readonly<Record<CompletenessArea, Completeness>>,
): CompletenessArea[] {
  return COMPLETENESS_AREA.filter((area) => completeness[area] === 'incomplete')
}

export function unknownAreas(
  completeness: Readonly<Record<CompletenessArea, Completeness>>,
): CompletenessArea[] {
  return COMPLETENESS_AREA.filter((area) => completeness[area] === 'unknown')
}

export const UNIT_ROLE_LABEL: Record<UnitRoleName, string> = {
  stock: 'Stok birimi',
  purchase: 'Alış birimi',
  sales: 'Satış birimi',
  price: 'Fiyat birimi',
  production: 'Üretim birimi',
  package: 'Paket birimi',
}

export const DETAIL_SECTION_LABEL: Record<DetailSection, string> = {
  template: 'Teknik şablon',
  units: 'Birim rolleri',
  packageStandards: 'Paket standartları',
  aliasHistory: 'Kod geçmişi',
}

export const MATCH_REASON_LABEL: Record<MatchReason, string> = {
  code: 'kod',
  alias: 'eski kod',
  name: 'ad',
}

/** Value shown where the server has no value. Never an empty cell. */
export const UNASSIGNED = 'atanmadı'
export const SECTION_UNAVAILABLE = 'Bu bölüm şu anda kullanılamıyor'
export const SECTION_UNAVAILABLE_DETAIL =
  'İlgili modül bu ortamda yayınlanmadığı için bölüm gösterilmiyor. Sayı veya özet üretilmez.'

export const PRODUCT_LIST_TEXT = {
  title: 'Ürünler',
  /**
   * §4: one global Product identity shared by both Göymen companies. Said on the
   * screen because the shell shows an active company selector two rows above, and
   * without this line an operator reasonably reads the list as "this company's
   * products" — then wonders why a colleague in the other company sees the same
   * codes.
   */
  scopeNote:
    'Ürün kartı iki Göymen şirketi için ortaktır. Şirkete özel fiyat, paket, stok ve etiket kayıtları bu listede yer almaz.',
  searchLabel: 'Kod, eski kod veya ad ara',
  searchHint: 'Eski koda göre arama, ürünün güncel kartını tek satır olarak getirir.',
  lifecycleLabel: 'Yaşam döngüsü',
  familyLabel: 'Ürün ailesi',
  rolesLabel: 'Roller',
  roleMatchLabel: 'Rol eşleşmesi',
  roleMatchAny: 'Herhangi biri',
  roleMatchAll: 'Hepsi',
  completenessLabel: 'Tamlık',
  completenessAll: 'Hepsi',
  completenessIncomplete: 'Yalnız eksik olanlar',
  clearFilters: 'Filtreleri temizle',
  pageSizeLabel: 'Sayfa boyutu',
  loadMore: 'Sonraki sayfa',
  loadingMore: 'Sonraki sayfa yükleniyor…',
  createAction: 'Yeni ürün',
  countScopeLabel: 'Filtrelenmiş, yetkili ve görünür ürünler',
  columns: {
    code: 'Kod',
    name: 'Ad',
    classification: 'Sınıf / aile',
    roles: 'Roller',
    stockUnit: 'Ana stok birimi',
    lifecycle: 'Durum',
    completeness: 'Tamlık',
    lastChanged: 'Son değişiklik',
  },
  /**
   * A next-page failure keeps the rows already on screen (§8.3: "sonraki sayfa
   * hatası mevcut listeyi korur"). Discarding them would punish the user for the
   * server's failure by throwing away the work they had already scrolled through.
   */
  nextPageFailed: 'Sonraki sayfa alınamadı. Görüntülenen satırlar korundu.',
  nextPageRetry: 'Sonraki sayfayı tekrar dene',
  invalidCursor: 'Sayfalama bağlantısı geçersiz',
  invalidCursorDetail:
    'Liste sunucuda değişmiş olabilir. Listeyi baştan yükleyerek devam edin.',
  invalidCursorAction: 'Listeyi baştan yükle',
} as const

export const PRODUCT_DETAIL_TEXT = {
  backToList: 'Ürün listesine dön',
  identityHeading: 'Kimlik ve durum',
  classificationHeading: 'Sınıflandırma ve roller',
  templateHeading: 'Teknik şablon',
  unitsHeading: 'Birim rolleri',
  packageHeading: 'Paket standartları',
  aliasHeading: 'Kod geçmişi',
  auditHeading: 'Değişiklik bilgisi',
  noAlias: 'Bu ürünün emekliye ayrılmış kodu yok.',
  aliasNote: 'Emekliye ayrılan kod tekrar kullanılamaz; arama bu karta yönlendirir.',
  createdAt: 'Oluşturuldu',
  updatedAt: 'Son güncelleme',
  lastActivatedAt: 'Son aktivasyon',
  lastDeactivatedAt: 'Son pasifleştirme',
  deactivationReason: 'Pasifleştirme gerekçesi',
  trackingPolicy: 'Takip politikası',
  /**
   * DEC-PRD-003, and the master/label distinction the product owner asked to keep
   * explicit: the Product card holds the POLICY, the physical package record holds
   * the lot, the origin and the quality/defect status.
   */
  trackingPolicyNote:
    'Ürün kartında yalnız takip politikası bulunur. Parti, menşe ve kalite/kusur durumu fiziksel etiket kaydında tutulur.',
  noTemplate: 'Bu ürüne teknik şablon bağlanmamış.',
  noProduct: 'Ürün bulunamadı',
} as const

export const PRODUCT_CREATE_TEXT = {
  title: 'Yeni ürün',
  /**
   * §6.1 and §6.2: create makes a DRAFT, and activation is a separate command with
   * its own audit record. Said before the form, not after the save, because it
   * changes what the user should expect the button to do.
   */
  draftNote:
    'Yeni ürün taslak olarak oluşturulur. Taslak kesin işlemlerde kullanılamaz; aktivasyon ayrı bir işlemdir.',
  /**
   * §7: the UI may show a placeholder but must not declare the final code before
   * the server answers. So this slice shows no code field at all and says why.
   */
  codeNote: 'Ürün kodunu sunucu üretir. Kod, kayıt tamamlandıktan sonra görünür.',
  nameLabel: 'Ürün adı',
  nameHint: 'Ad her iki Göymen şirketinde ortaktır.',
  classLabel: 'Ürün sınıfı',
  groupLabel: 'Ürün grubu',
  familyLabel: 'Ürün ailesi',
  optional: 'isteğe bağlı',
  rolesLabel: 'Ticari ve operasyonel roller',
  /**
   * The tri-state picker's reason, in the user's terms. Without it "Belirtilmedi"
   * looks like a lazy default rather than a deliberate, preserved fact.
   */
  rolesHint:
    'Rol sınıf ve gruptan bağımsızdır; birden fazla rol seçilebilir. Belirtilmeyen rol "değerlendirilmedi" olarak kalır, hayır olarak kaydedilmez.',
  roleYes: 'Evet',
  roleNo: 'Hayır',
  roleUnset: 'Belirtilmedi',
  trackingPolicyLabel: 'Takip politikası',
  submit: 'Taslak olarak kaydet',
  submitting: 'Kaydediliyor…',
  cancel: 'Vazgeç',
  errorSummaryTitle: 'Kayıt tamamlanamadı',
  errorSummaryIntro: 'Aşağıdaki alanları düzeltin:',
  createdTitle: 'Taslak kaydedildi',
  replayedNote: 'Bu istek daha önce işlenmişti; ikinci bir kayıt oluşturulmadı.',
  goToDetail: 'Ürün kartını aç',
  goToList: 'Listeye dön',
  validation: {
    nameRequired: 'Ürün adı zorunludur.',
    nameTooLong: 'Ürün adı en fazla 250 karakter olabilir.',
    classRequired: 'Ürün sınıfı zorunludur.',
    classTooLong: 'Ürün sınıfı en fazla 120 karakter olabilir.',
    groupTooLong: 'Ürün grubu en fazla 120 karakter olabilir.',
    familyTooLong: 'Ürün ailesi en fazla 120 karakter olabilir.',
    trackingPolicyTooLong: 'Takip politikası en fazla 120 karakter olabilir.',
  },
  /** §10.4 unsaved guard. */
  unsavedTitle: 'Kaydedilmemiş değişiklikler var',
  unsavedDetail: 'Sayfadan ayrılırsanız girdiğiniz bilgiler kaybolur.',
  unsavedStay: 'Sayfada kal',
  unsavedLeave: 'Değişiklikleri bırak',
  /**
   * §10.3 + AT-006: a failed save keeps the input. The sentence says so, because a
   * user who believes their typing is gone will retype it over the top of itself.
   */
  saveFailedRetained: 'Girdiğiniz bilgiler korundu; düzeltip tekrar kaydedebilirsiniz.',
  codeCollisionTitle: 'Bu kod zaten kullanılıyor',
  codeCollisionDetail:
    'Sunucu aynı kod veya eski kod için çakışma bildirdi. Kayıt oluşturulmadı.',
  unknownOutcomeTitle: 'Kayıt sonucu doğrulanamadı',
  /**
   * The unknown-outcome rule made readable: the attempt may have been processed, so
   * the user must NOT be told to "try again" in a way that implies a fresh record.
   * The same idempotency key is reused, which replays instead of duplicating.
   */
  unknownOutcomeDetail:
    'Sunucudan doğrulanabilir bir yanıt alınamadı. Aynı işlem tekrar denendiğinde yeni bir kayıt oluşmaz.',
  retrySameAttempt: 'Aynı işlemi tekrar dene',
} as const

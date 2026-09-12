/**
 * Turkish user-facing text for the Product screens.
 *
 * AGENTS.md: code and technical names are English, user text is Turkish and lives
 * in a source file that can be localized. Kept in ONE module so the list, the card
 * and the two forms cannot drift into calling the same fact different things — the
 * Cari slice learned that the hard way with its role labels.
 */

import {
  PRODUCT_ROLE,
  type CompletenessEvaluation,
  type ProductLifecycle,
  type ProductLifecycleValue,
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
 * What each state MEANS for the operator, not just what it is called.
 *
 * "Taslak" alone does not tell someone the product will not appear in an order
 * line; DEC-PRD-010 and §6.1 make that the whole point of the state.
 */
export const LIFECYCLE_MEANING: Record<ProductLifecycle, string> = {
  Draft: 'Kesin işlemlerde seçilemez.',
  Active: 'Yeni işlemlerde seçilebilir.',
  Inactive: 'Yeni kullanıma kapalı; geçmiş kayıtlarda görünür.',
}

/**
 * A status this build does not recognise.
 *
 * The raw token is printed beside it rather than hidden: an operator who can see
 * `bilinmeyen durum: foo` can report something actionable.
 */
export const LIFECYCLE_UNKNOWN_LABEL = 'Bilinmeyen durum'
export const LIFECYCLE_UNKNOWN_MEANING =
  'Bu durumu bu sürüm tanımıyor. Ürünün kullanılabilirliği hakkında bir sonuç çıkarmayın.'

export function lifecycleLabel(lifecycle: ProductLifecycleValue): string {
  return lifecycle.known
    ? LIFECYCLE_LABEL[lifecycle.value]
    : `${LIFECYCLE_UNKNOWN_LABEL}: ${lifecycle.raw}`
}

export function lifecycleMeaning(lifecycle: ProductLifecycleValue): string {
  return lifecycle.known ? LIFECYCLE_MEANING[lifecycle.value] : LIFECYCLE_UNKNOWN_MEANING
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
 * How a row's roles read in one line. Three outcomes, deliberately distinct:
 *  - at least one asserted role → those roles;
 *  - every role determined and none true → an explicit "no role" sentence;
 *  - some role still `not-evaluated` → said out loud.
 *
 * The third case is why this function exists: printing "Rol yok" for an
 * undetermined role would undo the contract's own distinction at the last step.
 */
export const NO_ROLE_TEXT = 'Ticari/operasyonel rol yok'
export const ROLE_UNEVALUATED_SUFFIX = 'değerlendirilmedi'

export function rolesText(roles: ProductRoles): string {
  const asserted = PRODUCT_ROLE.filter((name) => roles[name].value === true)
  const pending = PRODUCT_ROLE.filter((name) => roles[name].provenance === 'not-evaluated')
  const parts: string[] = []
  if (asserted.length > 0) parts.push(asserted.map((name) => ROLE_LABEL[name]).join(', '))
  else if (pending.length === 0) parts.push(NO_ROLE_TEXT)
  if (pending.length > 0) {
    parts.push(`${pending.map((name) => ROLE_LABEL[name]).join(', ')} — ${ROLE_UNEVALUATED_SUFFIX}`)
  }
  return parts.join(' · ')
}

/**
 * `ProductCompletenessView.Evaluation`.
 *
 * README: "NotEvaluated must not be rendered as complete or activatable." So the
 * three states read differently and an unknown token reads as unknown — never as
 * complete.
 */
export const COMPLETENESS_LABEL: Record<CompletenessEvaluation, string> = {
  Complete: 'Tam',
  Incomplete: 'Eksik',
  NotEvaluated: 'Değerlendirilmedi',
}
export const COMPLETENESS_UNKNOWN_LABEL = 'Bilinmeyen tamlık durumu'

export const UNIT_ROLE_LABEL: Record<UnitRoleName, string> = {
  stock: 'Stok birimi',
  purchase: 'Alış birimi',
  sales: 'Satış birimi',
  price: 'Fiyat birimi',
  production: 'Üretim birimi',
  package: 'Paket birimi',
}

/** Value shown where the server has no value. Never an empty cell. */
export const UNASSIGNED = 'atanmadı'

/**
 * Field-error codes → Turkish.
 *
 * The server sends `errors: [{field, code}]` — machine codes, not messages. These
 * are the codes visible in the published validator source. An UNKNOWN code must
 * still reach its input: `fieldErrorText` falls back to a general localized
 * sentence rather than dropping the error, because losing the field binding is
 * worse than losing message precision. The closed code set is an open request to
 * the contract owner.
 */
export const FIELD_ERROR_TEXT: Record<string, string> = {
  invalid_name: 'Ürün adı geçersiz.',
  invalid_role_provenance: 'Rol değeri veya kaynağı geçersiz.',
  invalid_reference: 'Seçilen referans geçersiz veya artık mevcut değil.',
  invalid_code_prefix: 'Seçilen kod grubunun ön eki geçersiz.',
  template_pair_required: 'Şablon ve şablon sürümü birlikte verilmelidir.',
  explicit_attribute_change_required: 'Şablon değişikliği için nitelikler açıkça verilmelidir.',
  null_not_allowed: 'Bu alan boş bırakılamaz.',
  required: 'Bu alan zorunludur.',
  invalid_metadata: 'İstek başlığı geçersiz.',
}

export const FIELD_ERROR_FALLBACK = 'Sunucu bu alanı reddetti.'

export function fieldErrorText(code: string): string {
  return FIELD_ERROR_TEXT[code] ?? FIELD_ERROR_FALLBACK
}

/**
 * What this slice cannot offer yet, and why — shown on screen rather than left as
 * a silent absence.
 *
 * The published contract accepts `classId`, `familyId` and the `references` block,
 * but publishes no read that would let the screen offer a valid value for any of
 * them. §19.3 forbids the client producing a reference, so the fields are absent.
 * Saying so is the difference between a known gap and a screen that looks finished.
 */
export const PENDING_REFERENCE_NOTE =
  'Sınıf, aile, şablon, birim ve takip politikası alanları bu sürümde sunulmuyor: sunucu bu referanslar için henüz bir okuma yayımlamadı ve istemci referans üretemez.'

export const PRODUCT_LIST_TEXT = {
  title: 'Ürünler',
  /**
   * §4: one global Product identity shared by both Göymen companies. Said on the
   * screen because the shell shows an active company selector two rows above, and
   * without this line an operator reads the list as "this company's products".
   */
  scopeNote:
    'Ürün kartı iki Göymen şirketi için ortaktır. Şirkete özel fiyat, paket, stok ve etiket kayıtları bu listede yer almaz.',
  /**
   * The list projection carries ids, not labels. Saying so keeps the narrow column
   * set from reading as "this product has no family".
   */
  projectionNote:
    'Liste bu sürümde kod, ad, roller, durum ve son değişikliği gösterir. Grup, sınıf ve aile adları ile birim/tamlık özeti sunucu projeksiyonunda henüz yok.',
  searchLabel: 'Kod, eski kod veya ad ara',
  lifecycleLabel: 'Yaşam döngüsü',
  roleLabel: 'Rol',
  /** One role, because the contract's query takes one. */
  roleHint: 'Tek rol seçilebilir; seçilen rolü taşıdığı bilinen ürünler listelenir.',
  roleAll: 'Hepsi',
  clearFilters: 'Filtreleri temizle',
  pageSizeLabel: 'Sayfa boyutu',
  loadMore: 'Sonraki sayfa',
  loadingMore: 'Sonraki sayfa yükleniyor…',
  createAction: 'Yeni ürün',
  columns: {
    code: 'Kod',
    name: 'Ad',
    roles: 'Roller',
    lifecycle: 'Durum',
    updatedAt: 'Son değişiklik',
  },
  /**
   * A next-page failure keeps the rows already on screen. Discarding them would
   * punish the user for the server's failure.
   */
  nextPageFailed: 'Sonraki sayfa alınamadı. Görüntülenen satırlar korundu.',
  nextPageRetry: 'Sonraki sayfayı tekrar dene',
  invalidCursor: 'Sayfalama bağlantısı geçersiz',
  invalidCursorDetail: 'Liste sunucuda değişmiş olabilir. Listeyi baştan yükleyerek devam edin.',
  invalidCursorAction: 'Listeyi baştan yükle',
  shown: (count: number) => `${count} kayıt görüntüleniyor`,
} as const

export const PRODUCT_DETAIL_TEXT = {
  backToList: 'Ürün listesine dön',
  edit: 'Düzenle',
  identityHeading: 'Kimlik ve durum',
  rolesHeading: 'Roller',
  referencesHeading: 'Teknik referanslar',
  completenessHeading: 'Tamlık',
  auditHeading: 'Değişiklik bilgisi',
  createdAt: 'Oluşturuldu',
  updatedAt: 'Son güncelleme',
  groupId: 'Kod grubu',
  classId: 'Sınıf',
  familyId: 'Aile',
  templateVersion: 'Şablon sürümü',
  trackingPolicy: 'Takip politikası',
  missingFields: 'Eksik alanlar',
  noMissingFields: 'Sunucu eksik alan bildirmedi.',
  /**
   * DEC-PRD-003, kept explicit: the card holds the POLICY reference; the lot, the
   * origin and the quality/defect status belong to the physical label record.
   */
  trackingPolicyNote:
    'Ürün kartında yalnız takip politikası referansı bulunur. Parti, menşe ve kalite/kusur durumu fiziksel etiket kaydında tutulur.',
  /** Ids without labels: said once rather than rendered as a bare UUID with no explanation. */
  referenceIdNote:
    'Aşağıdaki referanslar sunucudan kimlik olarak gelir; görünen adları için henüz bir katalog okuması yayımlanmadı.',
} as const

export const PRODUCT_CREATE_TEXT = {
  title: 'Yeni ürün',
  /**
   * §6.1/§6.2: create makes a DRAFT, and activation is a separate command with its
   * own audit record. Said before the form, because it changes what the user should
   * expect the button to do.
   */
  draftNote:
    'Yeni ürün taslak olarak oluşturulur. Taslak kesin işlemlerde kullanılamaz; aktivasyon ayrı bir işlemdir.',
  /** §7: the code is server-generated and is not declared before the response. */
  codeNote: 'Ürün kodunu sunucu, seçilen kod grubunun ön ekinden üretir. Kod kayıttan sonra görünür.',
  nameLabel: 'Ürün adı',
  nameHint: 'Ad her iki Göymen şirketinde ortaktır.',
  groupLabel: 'Kod grubu',
  groupHint: 'Ürün kodu bu grubun ön ekinden üretilir ve sonradan değişmez.',
  chooseNone: '— seçilmedi —',
  rolesLabel: 'Ticari ve operasyonel roller',
  rolesHint:
    'Rol gruptan bağımsızdır; birden fazla rol seçilebilir. Belirtilmeyen rol "değerlendirilmedi" olarak kalır, hayır olarak kaydedilmez.',
  roleYes: 'Evet',
  roleNo: 'Hayır',
  roleUnset: 'Belirtilmedi',
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
    groupRequired: 'Kod grubu seçilmelidir; ürün kodu bu gruptan üretilir.',
    groupUnknown: 'Seçilen kod grubu artık listede yok. Listeyi yenileyip tekrar seçin.',
  },
  /**
   * §19.3/§19.6: creation needs a real code group with a known prefix rule, and
   * there is no fallback the client may invent. Until that read succeeds the form
   * is visible but cannot be submitted, and it says why rather than failing on save.
   */
  groupsUnavailableTitle: 'Kod grubu bilgisi alınamadı',
  groupsUnavailableDetail:
    'Ürün kodu bir kod grubunun ön ekinden üretilir. Bu bilgi olmadan taslak oluşturulamaz.',
  groupsEmptyTitle: 'Tanımlı kod grubu yok',
  groupsEmptyDetail:
    'Yetkili olduğunuz kod grubu bulunamadı. Kod grubu tanımlanana kadar yeni ürün oluşturulamaz.',
  retryGroups: 'Tekrar dene',
  codeCollisionTitle: 'Kayıt reddedildi',
  codeCollisionDetail: 'Sunucu kod veya kimlik çakışması bildirdi. Kayıt oluşturulmadı.',
  unknownOutcomeTitle: 'Kayıt sonucu doğrulanamadı',
  /**
   * The unknown-outcome rule made readable: the attempt may have been processed, so
   * the user must NOT be told to "try again" in a way that implies a fresh record.
   */
  unknownOutcomeDetail:
    'Sunucudan doğrulanabilir bir yanıt alınamadı. Aynı işlem tekrar denendiğinde yeni bir kayıt oluşmaz.',
  retrySameAttempt: 'Aynı işlemi tekrar dene',
  saveFailedRetained: 'Girdiğiniz bilgiler korundu; düzeltip tekrar kaydedebilirsiniz.',
  unsavedTitle: 'Kaydedilmemiş değişiklikler var',
  unsavedDetail: 'Sayfadan ayrılırsanız girdiğiniz bilgiler kaybolur.',
  unsavedStay: 'Sayfada kal',
  unsavedLeave: 'Değişiklikleri bırak',
  contextChangedTitle: 'Şirket bağlamı değişti',
  contextChangedDetail:
    'Form önceki bağlamda açılmıştı. Girdiğiniz bilgiler gönderilmedi ve yeni bağlama taşınmadı; işlemi yeniden başlatın.',
} as const

export const PRODUCT_EDIT_TEXT = {
  title: 'Ürünü düzenle',
  /**
   * §4.1/§10.3: the card is one record shared by both Göymen companies, so a name
   * change is visible in both. Said before the fields, because it changes how
   * carefully the person should treat the form.
   */
  globalImpactNote:
    'Bu kart iki Göymen şirketi için ortaktır. Buradaki değişiklik her iki şirkette de görünür; bu nedenle gerekçe zorunludur.',
  diffNote: 'Yalnız değiştirdiğiniz alanlar kaydedilir; dokunmadığınız alanlar olduğu gibi kalır.',
  /** Basic edit is Draft-only and does not move the group or the code. */
  scopeNote: 'Temel düzenleme yalnız taslak kayıtta yapılır; kod grubu ve ürün kodu değişmez.',
  nameLabel: 'Ürün adı',
  rolesLabel: 'Ticari ve operasyonel roller',
  reasonLabel: 'Değişiklik gerekçesi',
  reasonHint: 'Gerekçe denetim kaydına yazılır.',
  derivedRoleNote: 'İlişkiden türetilen rol elle değiştirilemez.',
  submit: 'Değişiklikleri kaydet',
  submitting: 'Kaydediliyor…',
  cancel: 'Vazgeç',
  errorSummaryTitle: 'Değişiklik kaydedilemedi',
  errorSummaryIntro: 'Aşağıdaki alanları düzeltin:',
  savedTitle: 'Değişiklikler kaydedildi',
  currentVersion: 'Güncel sürüm',
  replayedNote: 'Bu istek daha önce işlenmişti; ikinci bir değişiklik uygulanmadı.',
  backToCard: 'Ürün kartına dön',
  /**
   * §19.5: a stale edit shows the current record and waits. It is NOT rebased or
   * overwritten on the user's behalf, and the copy must not offer to "force" it.
   */
  staleTitle: 'Kayıt siz düzenlerken değişti',
  staleDetail:
    'Bu ürün başka bir işlemle güncellendi. Değişiklikleriniz gönderilmedi ve kaydın üzerine yazılmadı. Güncel hâli yükleyip farkı gözden geçirin.',
  staleReadVersion: 'Okuduğunuz sürüm',
  staleCurrentVersion: 'Sunucudaki güncel sürüm',
  staleReload: 'Güncel hâli yükle',
  staleKeepDraft: 'Girdikleriniz aşağıda korunuyor.',
  lifecycleConflictTitle: 'Bu kayıt artık taslak değil',
  lifecycleConflictDetail:
    'Temel düzenleme yalnız taslak kayıtta yapılabilir. Değişiklikleriniz uygulanmadı.',
  notFoundTitle: 'Kayıt bulunamadı',
  notFoundDetail: 'Bu ürün artık bu bağlamda görünmüyor. Değişiklikleriniz uygulanmadı.',
  unknownOutcomeTitle: 'Değişiklik sonucu doğrulanamadı',
  unknownOutcomeDetail:
    'Sunucudan doğrulanabilir bir yanıt alınamadı. Aynı işlem tekrar denendiğinde ikinci bir değişiklik uygulanmaz.',
  retrySameAttempt: 'Aynı işlemi tekrar dene',
  saveFailedRetained: 'Girdiğiniz bilgiler korundu; düzeltip tekrar kaydedebilirsiniz.',
  unsavedTitle: 'Kaydedilmemiş değişiklikler var',
  unsavedDetail: 'Sayfadan ayrılırsanız yaptığınız değişiklikler kaybolur.',
  unsavedStay: 'Sayfada kal',
  unsavedLeave: 'Değişiklikleri bırak',
  contextChangedTitle: 'Şirket bağlamı değişti',
  contextChangedDetail:
    'Form önceki bağlamda açılmıştı. Değişiklikleriniz gönderilmedi ve yeni bağlama taşınmadı; kaydı yeniden açın.',
  validation: {
    nameRequired: 'Ürün adı zorunludur.',
    nameTooLong: 'Ürün adı en fazla 250 karakter olabilir.',
    reasonRequired: 'Değişiklik gerekçesi zorunludur.',
    reasonTooLong: 'Gerekçe en fazla 1000 karakter olabilir.',
    nothingChanged: 'Değişen bir alan yok.',
  },
} as const

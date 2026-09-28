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
  isHistoryField,
  type CompletenessEvaluation,
  type HistoryField,
  type HistoryOperationValue,
  type ProductFieldCode,
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
 * Shown where the product HAS a reference but the server could not resolve its
 * label. README makes this a different fact from "nothing assigned", so it reads
 * differently: one says nobody chose, the other says we could not look it up.
 */
export const LABEL_UNAVAILABLE = 'ad okunamadı'

export function labelOrUnavailable(label: string | null, id: string | null): string {
  if (label !== null) return label
  return id === null ? UNASSIGNED : LABEL_UNAVAILABLE
}

/**
 * `ProductFieldCodesV1.All` → Turkish, all thirteen.
 *
 * The server sends `errors: [{field, code}]` — machine codes, not messages — so the
 * sentence is produced here. The set is closed and published, and a test asserts
 * this map covers every member: a code the server can send but this build has no
 * sentence for would quietly fall back to the generic one, and the assertion is
 * what makes that drift visible instead of invisible.
 *
 * The fallback still exists and still matters: README asks for it explicitly, and a
 * v2 code arriving early must reach its input rather than be dropped. Losing the
 * FIELD binding is worse than losing message precision.
 */
export const FIELD_ERROR_TEXT: Record<ProductFieldCode, string> = {
  invalid_name: 'Ürün adı geçersiz.',
  invalid_role_provenance: 'Rol değeri veya kaynağı geçersiz.',
  invalid_reference: 'Seçilen referans geçersiz veya artık mevcut değil.',
  invalid_code_prefix: 'Seçilen kod grubunun ön eki geçersiz.',
  template_pair_required: 'Şablon ve şablon sürümü birlikte verilmelidir.',
  explicit_attribute_change_required: 'Şablon değişikliği için nitelikler açıkça verilmelidir.',
  null_not_allowed: 'Bu alan boş bırakılamaz.',
  required: 'Bu alan zorunludur.',
  invalid_metadata: 'İstek başlığı geçersiz.',
  invalid_reason: 'Değişiklik gerekçesi geçersiz.',
  invalid_version: 'Sürüm bilgisi geçersiz.',
  derived_role_readonly: 'İlişkiden türetilen rol elle değiştirilemez.',
  template_required: 'Bu işlem için şablon seçimi gereklidir.',
}

export const FIELD_ERROR_FALLBACK = 'Sunucu bu alanı reddetti.'

export function fieldErrorText(code: string): string {
  return FIELD_ERROR_TEXT[code as ProductFieldCode] ?? FIELD_ERROR_FALLBACK
}

/**
 * What this slice still does not offer, and why.
 *
 * Class and family ARE offered now — `reference-options` publishes them and they are
 * first-class members of the create request. The `references` block (template +
 * version, attributes, the six units, tracking policy) stays closed: §19.6 and the
 * coordinator keep attribute/unit completeness in the NEXT slice, so opening it here
 * would pull an unfinished decision forward rather than complete this one.
 */
export const PENDING_REFERENCE_NOTE =
  'Teknik alanlar yayımlanmış aile şablonundan gelir. Takip politikası henüz düzenlenemez; eksik taslak kullanıma onaylanmış ürün anlamına gelmez.'

/**
 * A catalogue the server could not read. Distinct from an empty one: README —
 * "An unavailable catalogue is different from a valid empty catalogue and must not
 * produce made-up choices."
 */
export const CATALOG_UNAVAILABLE = 'Bu katalog şu anda okunamıyor; seçim sunulmuyor.'
export const CATALOG_EMPTY = 'Bu katalogda tanımlı seçenek yok.'

export const PRODUCT_LIST_TEXT = {
  title: 'Ürünler',
  /**
   * §4: one global Product identity shared by both Göymen companies. Said on the
   * screen because the shell shows an active company selector two rows above, and
   * without this line an operator reads the list as "this company's products".
   */
  scopeNote:
    'Ürün kartı iki Göymen şirketi için ortaktır. Şirkete özel fiyat, paket, stok ve etiket kayıtları bu listede yer almaz.',
  searchLabel: 'Kod, eski kod veya ad ara',
  lifecycleLabel: 'Yaşam döngüsü',
  rolesLabel: 'Roller',
  roleMatchLabel: 'Rol eşleşmesi',
  roleAny: 'Herhangi biri',
  roleAll: 'Hepsi',
  /**
   * README: "A role filter matches known true values only… Unknown is not false."
   * Said on screen, because an operator filtering by "alınabilir" would otherwise
   * expect undetermined products to appear as not-matching rather than as absent.
   */
  roleHint:
    'Rol filtresi yalnız bilinen "evet" değerlerini eşler; değerlendirilmemiş rol eşleşme saymaz.',
  completenessLabel: 'Tamlık',
  sortLabel: 'Sıralama',
  directionLabel: 'Yön',
  directionAsc: 'Artan',
  directionDesc: 'Azalan',
  clearFilters: 'Filtreleri temizle',
  pageSizeLabel: 'Sayfa boyutu',
  loadMore: 'Sonraki sayfa',
  loadingMore: 'Sonraki sayfa yükleniyor…',
  createAction: 'Yeni ürün',
  columns: {
    code: 'Kod',
    name: 'Ad',
    classification: 'Sınıf / aile',
    roles: 'Roller',
    stockUnit: 'Ana stok birimi',
    lifecycle: 'Durum',
    completeness: 'Tamlık',
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
  primaryStockUnit: 'Ana stok birimi',
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
  /**
   * Unit references are still raw ids: `reference-options` publishes unit options,
   * but the unit EDITOR belongs to the next slice, so the card shows what the
   * `references` block carries rather than resolving names it was not given.
   */
  unitIdNote:
    'Birim referansları sunucudan kimlik olarak gelir; birim düzenleme sonraki dilimdedir.',
} as const

export const PRODUCT_ACTIVATION_TEXT = {
  submit: 'Ürünü aktifleştir',
  submitting: 'Aktifleştiriliyor…',
  globalImpact:
    'Aktifleştirme ürün kartını grup genelinde kesin işlemlerde kullanılabilir duruma getirir.',
  success: 'Ürün aktifleştirildi.',
  replayed: 'Bu aktivasyon daha önce işlenmişti; ikinci bir değişiklik uygulanmadı.',
  staleTitle: 'Ürün siz bakarken değişti',
  staleDetail: 'Aktivasyon uygulanmadı. Güncel kartı okuyup yeniden değerlendirin.',
  lifecycleTitle: 'Ürün artık taslak değil',
  lifecycleDetail: 'Aktivasyon yalnız taslak ürüne uygulanabilir.',
  notFound: 'Ürün artık bu bağlamda görünmüyor.',
  forbidden: 'Bu ürünü aktifleştirme yetkiniz yok.',
  unauthenticated: 'Oturum sonlandı; aktivasyon uygulanmadı.',
  failed: 'Aktivasyon uygulanmadı. Kartı yenileyip yeniden deneyin.',
  unknownTitle: 'Aktivasyon sonucu doğrulanamadı',
  unknownDetail:
    'İşlem uygulanmış olabilir. Aynı işlem anahtarıyla tekrar deneyerek sonucu güvenle doğrulayın.',
  retrySameAttempt: 'Aynı aktivasyonu tekrar dene',
  refresh: 'Kartı yenile',
  unsavedTitle: 'Sonucu bilinmeyen aktivasyon var',
  unsavedDetail: 'Sayfadan ayrılırsanız bu işlemin aynı anahtarla doğrulanması yarıda kalır.',
  unsavedStay: 'Sayfada kal',
  unsavedLeave: 'İşlemi bırak',
} as const

/**
 * Spec §10.2/§15: the server's exact-name duplicate preview. The texts state the ONE rule the server
 * applied (same name, Turkish case-insensitive) and what it does NOT do: it never blocks a save or an
 * approval, never merges, and does not compare technical attributes (no decided rule; NEEDS-SINAN).
 */
export const PRODUCT_DUPLICATE_TEXT = {
  heading: 'Mükerrer kart kontrolü',
  checking: 'Aynı adlı ürün kartları sunucuda kontrol ediliyor…',
  none: 'Sunucu kontrolünde bu adla başka ürün kartı yok.',
  foundTitle: 'Bu adla kayıtlı başka ürün kartları var',
  foundDetail:
    'Karşılaştırın: aynı ürünü ikinci kez tanımlamadığınızdan emin olun. Bu uyarı kaydı veya onayı engellemez ve kartları birleştirmez.',
  rule: 'Kontrol kuralı: aynı ad (Türkçe büyük/küçük harf farkı gözetilmez). Teknik özellik benzerliği bu kontrolde yoktur.',
  more: 'Aynı adlı daha fazla kart var; tamamı için ürün listesinde arayın.',
  failed: 'Aynı ad kontrolü yapılamadı. Bu, kaydı veya onayı engellemez.',
  changedBeforeApproval:
    'Aynı adlı kartların listesi siz bakarken değişti. Güncel listeyi inceleyin; onaylamak için yeniden basın.',
} as const

/**
 * Deactivation / reactivation of a card already in use (DEC-CAR-003, #98 §7.9). The texts say
 * what the move DOES in both companies and what it does NOT do: nothing is deleted, existing
 * stock, orders and history stay readable; only new selections refuse the card.
 */
export const PRODUCT_LIFECYCLE_TEXT = {
  deactivate: {
    open: 'Ürünü pasife al',
    submit: 'Pasife al',
    submitting: 'Pasife alınıyor…',
    impact:
      'Pasife alınan ürün her iki şirkette yeni sipariş, satın alma, stok ve üretim seçimlerinde kullanılamaz. Hiçbir kayıt silinmez; mevcut stok, sipariş ve geçmiş kayıtlar görünür kalır.',
    success: 'Ürün pasife alındı.',
    replayed: 'Bu pasife alma daha önce işlenmişti; ikinci bir değişiklik uygulanmadı.',
    lifecycleTitle: 'Ürün artık aktif değil',
    lifecycleDetail: 'Pasife alma yalnız aktif ürüne uygulanabilir.',
    forbidden: 'Bu ürünü pasife alma yetkiniz yok.',
    failed: 'Pasife alma uygulanmadı. Kartı yenileyip yeniden deneyin.',
    unknownTitle: 'Pasife alma sonucu doğrulanamadı',
    retrySameAttempt: 'Aynı pasife almayı tekrar dene',
    unsavedTitle: 'Sonucu bilinmeyen pasife alma var',
  },
  reactivate: {
    open: 'Ürünü yeniden aktifleştir',
    submit: 'Yeniden aktifleştir',
    submitting: 'Yeniden aktifleştiriliyor…',
    impact:
      'Yeniden aktifleştirilen ürün her iki şirkette yeni işlemlerde yeniden seçilebilir olur.',
    success: 'Ürün yeniden aktifleştirildi.',
    replayed: 'Bu yeniden aktifleştirme daha önce işlenmişti; ikinci bir değişiklik uygulanmadı.',
    lifecycleTitle: 'Ürün artık pasif değil',
    lifecycleDetail: 'Yeniden aktifleştirme yalnız pasif ürüne uygulanabilir.',
    forbidden: 'Bu ürünü yeniden aktifleştirme yetkiniz yok.',
    failed: 'Yeniden aktifleştirme uygulanmadı. Kartı yenileyip yeniden deneyin.',
    unknownTitle: 'Yeniden aktifleştirme sonucu doğrulanamadı',
    retrySameAttempt: 'Aynı yeniden aktifleştirmeyi tekrar dene',
    unsavedTitle: 'Sonucu bilinmeyen yeniden aktifleştirme var',
  },
  reasonLabel: 'Gerekçe',
  reasonHint: 'Gerekçe değişiklik geçmişine yazılır ve her iki şirkette görünür.',
  reasonRequired: 'Gerekçe zorunludur.',
  reasonTooLong: 'Gerekçe en fazla 1000 karakter olabilir.',
  reasonInvalid: 'Gerekçe kontrol karakteri içeremez.',
  cancel: 'Vazgeç',
  staleTitle: 'Ürün siz bakarken değişti',
  staleDetail: 'Değişiklik uygulanmadı. Güncel kartı okuyup yeniden değerlendirin.',
  notFound: 'Ürün artık bu bağlamda görünmüyor.',
  unauthenticated: 'Oturum sonlandı; değişiklik uygulanmadı.',
  unknownDetail:
    'İşlem uygulanmış olabilir. Aynı işlem anahtarıyla tekrar deneyerek sonucu güvenle doğrulayın.',
  refresh: 'Kartı yenile',
  unsavedDetail: 'Sayfadan ayrılırsanız bu işlemin aynı anahtarla doğrulanması yarıda kalır.',
  unsavedStay: 'Sayfada kal',
  unsavedLeave: 'İşlemi bırak',
  /** The Inactive banner's own line: when and why, from the newest deactivation in the history. */
  deactivatedAt: 'Pasife alındığı an',
  deactivationReason: 'Pasife alma gerekçesi',
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
  /**
   * §10.2 / CASE-PRD-U02-014: a name match WARNS and lets the person compare; it never
   * blocks or merges. The matches are the server's own search result for the typed
   * name, not a similarity score this client made up.
   */
  // The server matches the term in the name OR the code/alias; the title says so.
  nameMatchesChecking: 'Adında veya kodunda bu ifade geçen ürünler aranıyor…',
  nameMatchesTitle: 'Adında veya kodunda bu ifade geçen ürünler var',
  nameMatchesDetail:
    'Kaydı engellemez. Aynı ürünü ikinci kez oluşturmadığınızı kontrol edin; kod çakışması ayrıca sunucuda reddedilir.',
  nameMatchesSameName: 'aynı ad',
  nameMatchesMore: 'Daha fazla eşleşme var; tamamı için ürün listesinde arayın.',
  nameMatchesNone: 'Sunucu aramasında bu adı içeren ürün yok.',
  nameMatchesFailed: 'Benzer ad kontrolü yapılamadı. Bu, kaydı engellemez.',
  groupLabel: 'Kod grubu',
  groupHint: 'Ürün kodu bu grubun ön ekinden üretilir ve sonradan değişmez.',
  classLabel: 'Ürün sınıfı',
  familyLabel: 'Ürün ailesi',
  /** §19.3: an unresolved reference may stay empty on a Draft. */
  referenceHint: 'Taslakta boş bırakılabilir; sonradan tamamlanabilir.',
  chooseNone: '— seçilmedi —',
  chooseGroupFirst: 'Önce kod grubu seçin.',
  optional: 'isteğe bağlı',
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
    classUnknown: 'Seçilen ürün sınıfı artık listede yok. Listeyi yenileyip tekrar seçin.',
    familyUnknown: 'Seçilen ürün ailesi artık listede yok. Listeyi yenileyip tekrar seçin.',
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
  /**
   * Business #66 5706740724: a class-scoped reference read that is out or failed is not
   * evidence that the chosen class is gone. The choice stays; the save waits and says why.
   */
  referenceOptionsChecking: 'Seçenekler doğrulanıyor…',
  referenceOptionsHold: 'Sınıf/aile seçenekleri doğrulanıyor; tamamlanınca kaydedebilirsiniz.',
  referenceOptionsFailed: 'Sınıf/aile seçenekleri okunamadı; seçiminiz silinmedi.',
  referenceOptionsFailedHold:
    'Sınıf/aile seçenekleri okunamadı. Seçili sınıf veya aileyle kaydetmek için önce seçenekleri yeniden okuyun.',
  retryReferenceOptions: 'Seçenekleri yeniden oku',
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
  /**
   * Shown when a retry was refused but the FIRST send is still unaccounted for.
   * Saying "not created" there would be a claim the refusal does not support: the
   * server must re-authorize before it can read a stored result, so a 403 answering
   * the retry is a denial of replay access, not evidence of a rollback.
   */
  retryRefusedStillUnknown:
    'Tekrar denemesi reddedildi. Bu, ilk denemenin uygulanmadığı anlamına gelmez; sonucu hâlâ bilinmiyor. Yetkiniz geri verildiğinde aynı işlemle tekrar deneyin.',
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
  classLabel: 'Ürün sınıfı',
  familyLabel: 'Ürün ailesi',
  chooseNone: '— yok —',
  /** An explicit null clears a nullable reference; the copy says so plainly. */
  clearableHint: 'Boş bırakmak bağı kaldırır.',
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
  /**
   * Shown when a retry was refused but the FIRST send is still unaccounted for.
   * Saying "not created" there would be a claim the refusal does not support: the
   * server must re-authorize before it can read a stored result, so a 403 answering
   * the retry is a denial of replay access, not evidence of a rollback.
   */
  retryRefusedStillUnknown:
    'Tekrar denemesi reddedildi. Bu, ilk denemenin uygulanmadığı anlamına gelmez; sonucu hâlâ bilinmiyor. Yetkiniz geri verildiğinde aynı işlemle tekrar deneyin.',
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
    classUnknown: 'Seçilen ürün sınıfı artık listede yok. Listeyi yenileyip tekrar seçin.',
    familyUnknown: 'Seçilen ürün ailesi artık listede yok. Listeyi yenileyip tekrar seçin.',
    reasonRequired: 'Değişiklik gerekçesi zorunludur.',
    reasonTooLong: 'Gerekçe en fazla 1000 karakter olabilir.',
    nothingChanged: 'Değişen bir alan yok.',
  },
} as const

/* ── Change history (U-01 §9 item 6) ──────────────────────────────────────── */

export const PRODUCT_HISTORY_TEXT = {
  heading: 'Değişiklik geçmişi',
  loading: 'Değişiklik geçmişi yükleniyor…',
  /**
   * 404 AFTER the card loaded: the history read is not served here. Saying "not
   * found" would contradict the card the user is looking at.
   */
  unavailable: 'Değişiklik geçmişi bu ortamda henüz okunamıyor.',
  failed: 'Değişiklik geçmişi okunamadı.',
  malformed: 'Sunucudan gelen değişiklik geçmişi beklenen biçimde değil; gösterilmedi.',
  retry: 'Tekrar dene',
  reason: 'Gerekçe',
  changedFields: 'Değişen alanlar',
  noTrackedChange: 'İzlenen alanlarda değişiklik görünmüyor.',
  truncated: 'Yalnız son 50 değişiklik gösteriliyor.',
  /** The wire carries no actor; say so instead of leaving the reader to wonder. */
  actorNote: 'Değişikliği yapan kişi bu görünümde gösterilmez.',
  unknownOperation: 'Tanınmayan değişiklik türü',
  unknownField: 'Tanınmayan alan',
} as const

export function historyOperationLabel(operation: HistoryOperationValue): string {
  if (!operation.known) return `${PRODUCT_HISTORY_TEXT.unknownOperation} (${operation.raw})`
  switch (operation.value) {
    case 'DraftCreate':
      return 'Taslak oluşturuldu'
    case 'Activate':
      return 'Ürün aktifleştirildi'
    case 'Deactivate':
      return 'Ürün pasife alındı'
    case 'Reactivate':
      return 'Ürün yeniden aktifleştirildi'
    default:
      return 'Temel bilgiler düzenlendi'
  }
}

export function historyVersionText(before: string | null, after: string): string {
  return before === null ? `Sürüm ${after}` : `Sürüm ${before} → ${after}`
}

const HISTORY_FIELD_LABEL: Record<HistoryField, string> = {
  name: 'Ürün adı',
  classId: 'Sınıf',
  familyId: 'Aile',
  lifecycle: 'Durum',
  'roles.purchasable': `Rol: ${ROLE_LABEL.purchasable}`,
  'roles.sellable': `Rol: ${ROLE_LABEL.sellable}`,
  'roles.productionInput': `Rol: ${ROLE_LABEL.productionInput}`,
  'roles.productionOutput': `Rol: ${ROLE_LABEL.productionOutput}`,
  'references.templateId': 'Şablon',
  'references.templateVersion': 'Şablon sürümü',
  'references.attributes': 'Teknik nitelikler',
  'references.units.stock': UNIT_ROLE_LABEL.stock,
  'references.units.purchase': UNIT_ROLE_LABEL.purchase,
  'references.units.sales': UNIT_ROLE_LABEL.sales,
  'references.units.price': UNIT_ROLE_LABEL.price,
  'references.units.production': UNIT_ROLE_LABEL.production,
  'references.units.package': UNIT_ROLE_LABEL.package,
  'references.trackingPolicyId': 'Takip politikası',
}

export function historyFieldLabel(field: string): string {
  return isHistoryField(field) ? HISTORY_FIELD_LABEL[field] : `${PRODUCT_HISTORY_TEXT.unknownField} (${field})`
}

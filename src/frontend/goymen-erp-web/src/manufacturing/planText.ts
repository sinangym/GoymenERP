/**
 * MFG-PLAN create and detail screen text, in Turkish and in its own file. The edit form's own words and the plan
 * history stay in `./planEditText.ts`; the preview's in `./planPreviewText.ts`. `PLAN_FORM_TEXT` is what the create
 * and edit forms say alike (work center, parent-roll candidates, planned inputs, optional target).
 *
 * A pending command whose slot cannot be read, or that cannot be bound to the session, is named as such: nothing is
 * sent from the screen and the saved plans stay readable.
 */

export const PLAN_FORM_TEXT = {
  eyebrow: 'ÜRETİM PLANI',
  backToOrders: '← Üretim emirleri',
  workCenter: 'İş merkezi',
  workCentersLoading: 'İş merkezleri yükleniyor…',
  inactiveSuffix: ' (pasif)',
  parentRoll: 'Ana rulo',
  rollsLoading: 'Ana rulolar yükleniyor…',
  plannedInputs: 'Planlanan girdiler',
  label: 'Etiket',
  labelVersion: 'Etiket sürümü',
  plannedKg: 'Planlanan kg',
  plannedKgFor: (name: string) => `${name} planlanan kg`,
  physicalKg: 'Fiziksel kg',
  choice: 'Seçim',
  action: 'İşlem',
  add: 'Ekle',
  unselectable: 'Seçilemez',
  remove: 'Kaldır',
  optionalTarget: 'İsteğe bağlı hedef',
  targetBasis: 'Hedef türü',
  targetNone: 'Hedef yok',
  targetKg: 'Hedef kg',
  formRequired: 'İş merkezi, en az bir ana rulo ve her rulo için sıfırdan büyük planlanan kg zorunludur.',
  targetPair: 'Hedef kullanılacaksa türü ve sıfırdan büyük kg değeri birlikte girilmelidir.',
  slitting: 'Dilme ayrıntısı',
  sequence: 'Sıra',
} as const

/** A reference read (work centers, parent-roll candidates) that did not answer, in the user's terms. */
export function readFailureText(subject: string, outcome: { readonly type: string; readonly status?: number }): string {
  if (outcome.type === 'http') {
    if (outcome.status === 401 || outcome.status === 403) return `${subject} için yetkiniz artık geçerli değil.`
    return `${subject} kaynağı yanıt vermedi (${outcome.status}).`
  }
  if (outcome.type === 'offline') return 'Çevrimdışısınız.'
  if (outcome.type === 'fence-stale') return 'Şirket veya oturum değişti; eski sonuç kullanılmadı.'
  return outcome.type === 'malformed'
    ? `${subject} kaynağı beklenen sözleşmeyle yanıt vermedi.`
    : `${subject} kaynağına ulaşılamadı.`
}

export const PLAN_CREATE_TEXT = {
  heading: 'Yeni taslak plan',
  unknown: 'Önceki gönderimin sonucu bilinmiyor.',
  blocked:
    'Bu şirket için bu oturumda bekleyen bir plan kaydı okunamadı. Önceki gönderimin sonucu bilinmediği için bu '
    + 'ekrandan yeni plan gönderilmez; kayıtlı planlar okunabilir.',
  noSession:
    'Sunucu bu oturum için oturum bağlayıcısı vermedi. Bekleyen bir plan kaydının bu oturuma ait olduğu doğrulanamayacağı '
    + 'için bu ekrandan yeni plan gönderilmez; kayıtlı planlar okunabilir.',
  notDurable:
    'Plan, sayfa yenilense de aynı istek ve anahtarla yeniden gönderilebilecek biçimde saklanamadı; istek gönderilmedi.',
  sendUnknown: 'Sonuç bilinmiyor. Aynı içerik ve anahtarla yeniden gönderin.',
  noContext: 'Yetkili tek şirket bağlamı seçilmeden plan oluşturulamaz.',
  savedTitle: 'Taslak plan kaydedildi',
  savedReference: (planId: string, version: number) => `${planId} · sürüm ${version}`,
  replayed: 'Önceki kayıtlı sonuç yeniden gösterildi.',
  reopen: 'Formu kapat ve planı yeniden aç',
  backToOrders: 'Üretim emirlerine dön',
  unresolvedNote: 'Önceki gönderim kesinleşmeden yeni plan oluşturulamaz.',
  resend: 'Aynı anahtarla yeniden gönder',
  closeForm: 'Formu kapat',
  preselectLoading: 'Stok pozisyonundan seçilen ana rulo aranıyor…',
  preselectAdded: (labelCode: string) =>
    `Stok pozisyonundan seçilen ana rulo eklendi: ${labelCode}. Planlanan kg’ı girin; seçim kayıtta etiket sürümüyle yeniden doğrulanır.`,
  preselectRefused: (labelCode: string, code: string) =>
    `Stok pozisyonundan seçilen etiket (${labelCode}) taslak plana eklenemez (${code}).`,
  preselectMissing: (windowFull: boolean) =>
    `Stok pozisyonundan seçilen etiket ana rulo adayları arasında bulunamadı${windowFull ? ' (arama penceresi doldu)' : ''}; etiketi elle arayın.`,
  choose: 'Seçiniz',
  candidates: 'Ana rulo adayları',
  candidatesNote:
    'Blok dışı fiziksel kg kullanılabilirlik veya rezervasyon garantisi değildir. Seçim, kayıtta etiket sürümüyle yeniden doğrulanır.',
  search: 'Etiket, ürün veya lot ara',
  searchAction: 'Ara',
  productLot: 'Ürün / lot',
  document: 'Belge',
  lot: (lotId: string) => `Lot ${lotId}`,
  candidateBalances: (unblockedKg: string, blockedKg: string) => `Blok dışı: ${unblockedKg} kg · Blokeli: ${blockedKg} kg`,
  added: 'Eklendi',
  searchWindowFull: 'Arama penceresi doldu; sonucu daraltın.',
  noInputs: 'Henüz ana rulo eklenmedi.',
  addSlitting: 'Sıralı bant planı ekle',
  slittingNote: 'Satırlar kaydedildikleri sırayla korunur. Genişlik mm, adet tam sayıdır.',
  bandWidthMm: 'Bant genişliği (mm)',
  count: 'Adet',
  band: (position: number) => `Bant ${position}`,
  bandWidthFor: (position: number) => `Bant ${position} genişliği mm`,
  bandCountFor: (position: number) => `Bant ${position} adedi`,
  addBand: 'Bant satırı ekle',
  windingTarget: 'İsteğe bağlı sarım hedefi (kg)',
  saving: 'Kaydediliyor…',
  save: 'Taslak planı kaydet',
} as const

/** The plan save's refusal, in the user's terms. */
export function saveFailureText(status: number, code: string | null): string {
  if (status === 401 || status === 403) return 'Plan kaydetme yetkiniz artık geçerli değil.'
  if (status === 404) return 'Seçilen ana rulo veya şirket bağlamı artık bulunamıyor.'
  if (status === 409) return `Seçilen kayıtların sürümü veya yazma durumu değişti (${code ?? 'conflict'}).`
  if (status === 422) return `Plan girdisi kabul edilmedi (${code ?? 'rule_refusal'}).`
  return `Plan kaydedilmedi (${code ?? status}).`
}

export const PLAN_DETAIL_TEXT = {
  heading: 'Plan yeniden açıldı',
  contextSubject: 'üretim planı için tek şirket bağlamı',
  subject: 'üretim planı',
  backToPlans: '← Üretim planları',
  blocked:
    'Bu plan için bu oturumda bekleyen bir komut kaydı okunamadı. Önceki işlemin sonucu bilinmediği için bu ekrandan '
    + 'yeni düzenleme, onay veya emir gönderilmez; plan yalnız okunabilir.',
  noSession:
    'Sunucu bu oturum için oturum bağlayıcısı vermedi. Bekleyen bir komut kaydının bu oturuma ait olduğu doğrulanamayacağı '
    + 'için bu ekrandan düzenleme, onay veya emir gönderilmez; plan yalnız okunabilir.',
  notDurable:
    'Düzenleme, sayfa yenilense de aynı istek ve anahtarla yeniden gönderilebilecek biçimde saklanamadı; istek gönderilmedi.',
  approvedRefreshed: 'Plan onaylandı ve güncel kayıt yeniden okundu.',
  approvedRefreshPending: 'Plan onaylandı; güncel kayıt henüz yeniden okunamadı.',
  conflictRefreshed: (failure: string) => `${failure} Güncel plan yeniden okundu.`,
  conflictRefreshPending: (failure: string) => `${failure} Güncel plan henüz yeniden okunamadı.`,
  orderReplayed: 'Önceki emir oluşturma sonucu yeniden gösterildi.',
  orderCreated: 'Üretim emri oluşturuldu.',
  plan: 'Plan',
  status: 'Durum',
  salesOrderLink: 'Satış sipariş bağı',
  salesOrderLinkNone: 'Bağlı sipariş satırı yok',
  salesOrderLinkLine: 'satır',
  salesOrderLinkRevision: 'satır revizyonu',
  version: 'Sürüm',
  process: 'Proses',
  target: 'Hedef',
  inputsHeading: 'Planlanan ana rulolar',
  rollLabel: 'Ana rulo etiketi',
  technicalSource: 'Yakalanan teknik kaynak',
  noTechnicalSnapshot: 'Yok — eski plan, güncel üründen tamamlanmaz',
  product: 'Ürün',
  productVersion: 'sürüm',
  family: 'Aile',
  template: 'Şablon',
  nominalLength: 'Nominal ürün boyu:',
  notRecorded: 'kaydedilmemiş',
  kgProfile: 'kg profili',
  bandWidth: 'Bant genişliği',
  windingTarget: (value: string) => `Sarım hedefi: ${value}`,
  none: 'Yok',
  cutToLength: 'Boy kesme ayrıntısı',
  calculationHeading: 'Kayıtlı plan hesaplaması',
  noCalculation: 'Bu eski komut yanıtında kalıcı hesaplama projeksiyonu bulunmuyor.',
  nextHeading: 'Sonraki işlem',
  resend: 'Aynı istek ve anahtarla yeniden gönder',
  reread: 'Güncel planı yeniden oku',
  orderSuffix: (orderId: string) => ` Emir: ${orderId}`,
  approving: 'Plan onaylanıyor…',
  approve: 'Planı onayla',
  creatingOrder: 'Üretim emri oluşturuluyor…',
  createOrder: 'Üretim emri oluştur',
  approvedReadOnly: 'Onaylı plan bu ekranda salt okunurdur.',
  draftReadOnly: 'Taslak plan bu ekranda salt okunurdur.',
  reject: 'Planı reddet',
  rejecting: 'Plan reddediliyor…',
  cancel: 'Planı iptal et',
  cancelling: 'Plan iptal ediliyor…',
  selfApprovalNote: 'Bu sürümü siz hazırladınız; hazırlayan onaylayamaz veya reddedemez. Kararı başka bir kullanıcı vermelidir.',
  reasonLabel: 'Gerekçe',
  reasonHint: 'En fazla 256 karakter; satır sonu kullanılamaz.',
  reasonInvalid: 'Gerekçe boş olamaz, en fazla 256 karakter olabilir ve denetim karakteri içeremez.',
  rejectConfirm: 'Reddi gönder',
  cancelConfirm: 'İptali gönder',
  dismiss: 'Vazgeç',
  rejectedRefreshed: 'Plan reddedildi; taslak olarak kaldı ve güncel kayıt yeniden okundu.',
  rejectedRefreshPending: 'Plan reddedildi; güncel kayıt henüz yeniden okunamadı.',
  cancelledRefreshed: 'Plan iptal edildi ve güncel kayıt yeniden okundu.',
  cancelledRefreshPending: 'Plan iptal edildi; güncel kayıt henüz yeniden okunamadı.',
  cancelledReadOnly: 'İptal edilmiş plan bu ekranda salt okunurdur.',
  cancelledBy: 'İptal gerekçesi',
  rejectionsHeading: 'Önceki redler',
  rejectionLine: (number: number, reason: string, planVersion: number) => `${number}. ret (sürüm ${planVersion}): ${reason}`,
} as const

type LifecycleKind = 'approve' | 'create-order' | 'reject' | 'cancel'

/** An approval's or order creation's refusal, in the user's terms (a revision's is `revisionFailureText`). */
export function lifecycleFailureText(kind: LifecycleKind, status: number, code: string | null): string {
  const subject = kind === 'approve' ? 'Plan onayı'
    : kind === 'reject' ? 'Plan reddi'
      : kind === 'cancel' ? 'Plan iptali'
        : 'Üretim emri'
  if (status === 401 || status === 403) return `${subject} yetkiniz artık geçerli değil.`
  if (status === 404) return `${subject} için kayıt bu şirket bağlamında bulunamadı.`
  // U3-a (URT-D21, URT-D3): the approval allocates the plan's inputs in Inventory; its two common refusals are named.
  if (status === 409 && code === 'inventory_manufacturing_allocation_insufficient_free_stock') {
    return 'Plan girdileri için stokta yeterli serbest miktar yok; plan onaylanmadı.'
  }
  if (status === 409 && code === 'inventory_manufacturing_parent_label_version_conflict') {
    return 'Girdi etiketi onay sırasında değişti; plan onaylanmadı. Yeniden deneyin.'
  }
  if (status === 409) return `${subject} uygulanmadı (${code ?? 'conflict'}).`
  // U2-d (URT-D2): the preparer of the current version may not approve it; another user decides.
  if (status === 422 && code === 'manufacturing_plan_self_approval') {
    return 'Planın bu sürümünü siz hazırladınız; onayı başka bir kullanıcı vermelidir.'
  }
  if (status === 422 && code === 'manufacturing_plan_has_order') {
    return 'Bu plandan üretim emri açıldığı için plan iptal edilemez.'
  }
  if (status === 422 && code === 'manufacturing_plan_cancelled') {
    return 'Plan zaten iptal edilmiş; yeni karar verilemez.'
  }
  if (status === 400 && (kind === 'reject' || kind === 'cancel')) {
    return `${subject} gerekçesi veya sürümü sunucu tarafından kabul edilmedi.`
  }
  if (status === 422) return `${subject} iş kuralı tarafından reddedildi (${code ?? 'rule_refusal'}).`
  return `${subject} uygulanmadı (${code ?? status}).`
}

export function lifecycleUnknownText(kind: LifecycleKind): string {
  const subject = kind === 'approve' ? 'Plan onayının'
    : kind === 'reject' ? 'Plan reddinin'
      : kind === 'cancel' ? 'Plan iptalinin'
        : 'Emir oluşturmanın'
  return `${subject} sonucu bilinmiyor. Aynı istek ve anahtarla yeniden gönderin.`
}

/**
 * User-facing text for the C-02 create screen and the CARI-LINK workspace.
 * Turkish, kept in one resource module so it is localizable as a unit
 * (AGENTS.md: kullanıcı metinleri Türkçe ve kaynak dosyalı). No string below
 * carries a raw id, a capability id or a server-side vocabulary token.
 */

import type { AttemptFailure, AttemptUnknownReason } from './commandAttempt'

export const CARI_ROLE_LABEL: Record<string, string> = {
  Customer: 'Müşteri',
  Supplier: 'Tedarikçi',
  Tolling: 'Fason',
  Service: 'Hizmet',
}

export const CARI_KIND_LABEL: Record<number, string> = {
  1: 'Tüzel kişi',
  2: 'Gerçek kişi',
}

export const CARI_CREATE_TEXT = {
  title: 'Yeni Cari',
  backToList: '← Cari Kartlar listesine dön',
  formLegend: 'Cari bilgileri',
  activeCompany: 'Aktif şirket',
  activeCompanyUnknown: 'Şirket adı çözülüyor…',
  kind: 'Taraf türü',
  kindPlaceholder: 'Seçin',
  legalName: 'Hukuki ad',
  tradeName: 'Ticari ad',
  optional: 'isteğe bağlı',
  roles: 'Roller',
  submit: 'Cari oluştur',
  submitting: 'Gönderiliyor…',
  errorSummary: 'Form gönderilemedi; işaretli alanları düzeltin.',
  validation: {
    kind: 'Taraf türü seçin.',
    legalNameRequired: 'Hukuki ad zorunludur.',
    legalNameTooLong: 'Hukuki ad boşluklar kırpıldıktan sonra en çok 250 karakter olabilir.',
    tradeNameTooLong: 'Ticari ad boşluklar kırpıldıktan sonra en çok 200 karakter olabilir.',
    roles: 'En az bir rol seçin.',
  },
  successTitle: 'Cari oluşturuldu',
  successCode: 'Cari kodu',
  successCompany: 'Şirket hesabı',
  successRoles: 'Roller',
  successNoDetail:
    'Cari 360 ekranı bu şirket bağlamında açık değil; kaydı Cari Kartlar listesinden görüntüleyin.',
  createAnother: 'Yeni cari oluştur',
  announceCreated: (code: string) => `Cari ${code} oluşturuldu.`,
  noSingleCompany: 'yeni cari için tek şirket bağlamı',
  subject: 'yeni cari',
} as const

export const CARI_LINK_TEXT = {
  title: 'Cariyi Şirkete Bağla',
  backToList: '← Cari Kartlar listesine dön',
  targetCompany: 'Hedef şirket',
  targetCompanyUnknown: 'Şirket adı çözülüyor…',
  intro:
    'Başka bir yetkili şirketten görülebilen, hedef şirkete henüz bağlı olmayan cariyi seçin ve hedef şirketteki rollerini belirleyin. Yeni cari kaydı oluşturulmaz.',
  searchLabel: 'Aday ara',
  searchPlaceholder: 'Kod, unvan veya ticari unvan',
  searchButton: 'Ara',
  candidatesCaption: 'Hedef şirkete bağlanabilecek adaylar',
  candidatesRegion: 'Aday listesi tablosu',
  colSelect: 'Seç',
  colCode: 'Kod',
  colName: 'Unvan',
  colKind: 'Tür',
  colSources: 'Görünür kaynak hesaplar',
  sourceActive: 'Aktif',
  sourceInactive: 'Pasif',
  unknownCompany: 'Yetkili şirket',
  loadMore: 'Daha fazla aday yükle',
  loading: 'Yükleniyor…',
  continuationFailed: 'Daha fazla aday yüklenemedi; mevcut adaylar korunuyor.',
  retry: 'Tekrar dene',
  cursorRecovered: 'Aday listesi yenilendi; ilk sayfaya dönüldü.',
  candidatesRefreshed: 'Aday listesi yenilendi.',
  selectedCandidate: 'Seçili aday',
  noSelection: 'Henüz aday seçilmedi.',
  selectionSuspended: 'Seçim güncel aday listesiyle henüz doğrulanmadı.',
  formHeading: 'Hedef şirketteki roller',
  rolesLegend: 'Hedef şirket için roller',
  submit: 'Şirkete bağla',
  submitting: 'Gönderiliyor…',
  errorSummary: 'Bağlama gönderilemedi; işaretli alanları düzeltin.',
  validation: {
    selection: 'Güncel aday listesinden bir cari seçin.',
    roles: 'Hedef şirket için en az bir rol seçin.',
  },
  successTitle: 'Cari hedef şirkete bağlandı',
  successCode: 'Cari kodu',
  successCompany: 'Hedef şirket hesabı',
  successRoles: 'Hedef şirketteki roller',
  successNoDetail:
    'Cari 360 ekranı bu şirket bağlamında açık değil; kaydı Cari Kartlar listesinden görüntüleyin.',
  linkAnother: 'Başka cari bağla',
  verifyInDetail: 'Güncel durumu Cari 360 ile doğrula',
  announceLinked: (code: string) => `Cari ${code} hedef şirkete bağlandı.`,
  noSingleCompany: 'şirkete bağlama için tek hedef şirket bağlamı',
  subject: 'aday listesi',
  linkSubject: 'şirkete bağlama',
  candidateLabel: (code: string, legalName: string) => `${code} — ${legalName}`,
} as const

/**
 * The Cari card (C-03). The two section notes are the load-bearing text here: the
 * handoff requires the user to be able to tell SHARED party identity from what
 * belongs only to the selected company's account, because that distinction decides
 * which of the two is about to be changed and who else sees it.
 */
export const CARI_CARD_TEXT = {
  identityHeading: 'Kimlik',
  identityNote: 'Bütün yetkili şirketlerde aynı olan ortak taraf kimliği.',
  accountHeading: 'Bu şirketteki hesap',
  accountNote: 'Yalnız seçili şirketin hesabına ait bilgiler.',
  editContact: 'İletişim ve adres bilgilerini düzenle',
  openLedger: 'Cari hesap ekstresini aç',
} as const

/**
 * C-03's client-contract refusal, shared by the card's own read and the company
 * relationships panel's group read. It is one fact about THIS client in both places — never
 * about the record — so it has one wording and one recovery: reload, not resend.
 */
export const CARI_CONTRACT_REFUSAL_TEXT = {
  title: 'Uyumlu istemci gerekli',
  detail:
    'Bu cari işlemi desteklenen istemci sözleşmesi ile yeniden açılmalıdır. ' +
    'Sayfayı yenileyin; bekleyen bir işlemi otomatik tekrarlamayın.',
} as const

/**
 * The C-03 company relationships panel (spec §14.2 "Aynı Party'nin şirket ilişkilerini gör").
 *
 * The note carries the rule the panel exists to make visible: the Party is one, the
 * accounts are per company, and an account's state is a fact about ITS company only.
 * "Okuma yetkiniz olan" is precise, not modest: the server lists exactly the companies
 * the actor may read in, so a company missing here is not a claim that no account exists.
 */
export const CARI_RELATIONSHIPS_TEXT = {
  heading: 'Şirket ilişkileri',
  note:
    'Aynı cari kimliğinin, okuma yetkiniz olan şirketlerdeki hesapları. ' +
    'Her hesabın durumu yalnız kendi şirketine aittir.',
  show: 'Diğer şirketlerdeki hesapları göster',
  hide: 'Şirket ilişkilerini gizle',
  subject: 'şirket ilişkileri',
  caption: 'Bu carinin şirket hesapları',
  columnCompany: 'Şirket',
  columnState: 'Şirket hesabı durumu',
  columnAction: 'İşlem',
  current: 'Şu an açık olan şirket',
  openIn: (companyLabel: string) => `${companyLabel} şirketinde aç`,
  onlyThisCompany:
    'Okuma yetkiniz olan diğer şirketlerde bu carinin hesabı görünmüyor.',
} as const

/**
 * C-06 — the selected company's operational contact and address editor.
 *
 * Every problem message says what the client actually knows. The e-mail and country
 * lines are deliberate about their own limits: a format check is not a claim that the
 * address receives mail or that the country exists in any registry.
 */
export const CARI_CONTACT_TEXT = {
  title: 'Cari Bilgileri — İletişim ve Adres',
  backToCard: '← Cari karta dön',
  backToList: '← Cari Kartlar listesine dön',
  scopeNote:
    'Bu bölüm yalnız seçili şirketin hesabına aittir. Ortak taraf kimliği (unvan, resmi kimlik, sicil) burada değişmez.',
  activeCompany: 'İşlem şirketi',
  subject: 'iletişim ve adres bilgileri',
  noSingleCompany: 'iletişim düzenlemesi için tek şirket bağlamı',
  contactHeading: 'Operasyonel iletişim',
  addressHeading: 'Adresler',
  contactsHeading: 'İrtibat kişileri',
  channelsHeading: 'İletişim kanalları',
  addAddress: 'Adres ekle',
  addContact: 'İrtibat kişisi ekle',
  addChannel: 'Kanal ekle',
  deactivate: 'Pasife al',
  reactivate: 'Yeniden etkinleştir',
  undoState: 'Durum değişikliğini geri al',
  passive: 'Pasif',
  markedDeactivate: 'Kaydedildiğinde pasife alınacak.',
  markedReactivate: 'Kaydedildiğinde yeniden etkinleştirilecek.',
  newRow: 'Yeni satır',
  /** Only ever offered for a row the user just added; an existing row is deactivated. */
  removeRow: 'Satırı kaldır',
  statusBlockedByEdits:
    'Bu satırda kaydedilmemiş alan değişikliği var; durum işlemi ayrı bir kayıttır. Önce alanları kaydedin ya da değişikliği geri alın.',
  fieldsFrozenByStatus:
    'Durum işlemi yalnız satırın durumunu değiştirir; bu kayıtta alanlar gönderilmez.',
  noAddresses: 'Bu şirket hesabında kayıtlı adres yok.',
  noContacts: 'Bu şirket hesabında kayıtlı irtibat kişisi yok.',
  save: 'Bilgileri kaydet',
  saving: 'Gönderiliyor…',
  nothingToSave: 'Kaydedilecek değişiklik yok.',
  errorSummary: 'Bilgiler kaydedilemedi; işaretli alanları düzeltin.',
  savedTitle: 'Bilgiler kaydedildi',
  savedDetail: 'Kayıt sunucudan yeniden okundu; aşağıdaki değerler güncel hâlidir.',
  refreshRecord: 'Kaydı yenile',
  refreshedTitle: 'Kayıt yenilendi',
  refreshedDetail:
    'Sunucudaki güncel sürüm alındı. Yalnız sizin değiştirdiğiniz alanlar korundu; dokunmadığınız alanlar sunucudaki güncel hâliyle güncellendi. Değişikliklerinizi gözden geçirip yeniden kaydedin.',
  revalidating: 'Önceki işlem yeniden denenmeden önce kayıt yeniden okunuyor…',
  readRecovery:
    'İşlem sunucuya iletildi, ancak kaydın güncel hâli okunamadı. Aşağıdan yeniden okuyun; okuma başarılı olana kadar yeni kayıt gönderilmez.',
  announceSaved: 'İletişim ve adres bilgileri kaydedildi.',
  retainedNote: (count: number) =>
    `Bu ekranda görünmeyen ${count} kayıt korunuyor; gönderilmeyen satır silinmez.`,
  optional: 'isteğe bağlı',
  selectPlaceholder: 'Seçin',
  /** A value the server holds that is not in the published set. Kept, never rewritten. */
  keepCurrent: (value: string) => `${value} (mevcut değer)`,
  field: {
    phone: 'Telefon',
    phoneExtension: 'Dahili',
    email: 'E-posta',
    website: 'Web adresi',
    purpose: 'Kullanım amacı',
    countryCode: 'Ülke kodu',
    line1: 'Adres satırı 1',
    line2: 'Adres satırı 2',
    city: 'İl',
    district: 'İlçe',
    postalCode: 'Posta kodu',
    displayName: 'Ad soyad',
    jobTitle: 'Görev',
    contactPurpose: 'Amaç',
    kind: 'Kanal türü',
    value: 'Değer',
    extension: 'Dahili',
  },
  problem: {
    required: 'Bu alan zorunludur.',
    tooLong: (max: number) => `Boşluklar kırpıldıktan sonra en çok ${max} karakter olabilir.`,
    tooLongUnknown: 'Değer sunucunun kabul ettiği uzunluğu aşıyor.',
    emailFormat: 'E-posta biçimi geçersiz. Biçim kontrolü adresin ulaşılabilirliğini doğrulamaz.',
    urlUnsupported: 'Yalnız http veya https ile başlayan tam adres kabul edilir.',
    notAllowed: 'Listedeki değerlerden birini seçin.',
    countryFormat: 'İki büyük harf kullanın (örnek: TR). Ülke kaydı doğrulanmaz.',
    notClearable: 'Bu alan boş bırakılamaz.',
    criticalScope: 'Bu alan buradan kaydedilmez; ayrı onay akışı gerektirir.',
    unknownField: 'Bu alan sunucu sözleşmesinde yok.',
    rowIdentity: 'Satır kimliği eksik; satırı kaldırıp yeniden ekleyin.',
    rowEmpty: 'Eklenen satırı doldurun ya da kaldırın.',
    immutable: 'Bu alan kayıtlı satırda değiştirilemez; bunun yerine yeni bir satır ekleyin.',
    controlCharacters:
      'Değerin içinde satır sonu veya görünmeyen denetim karakteri var; bunları kaldırın.',
    notApplicable:
      'Dahili numara yalnız bir telefonla birlikte kaydedilir. Telefonu doldurun ya da dahiliyi temizleyin.',
    rowDuplicate: 'Aynı kayıt için ikinci bir işlem var; satırlardan birini kaldırın.',
    statusNeedsExisting: 'Durum değişikliği yalnız kayıtlı satırlar için yapılabilir.',
    tooManyOperations:
      'Tek kaydetmede bu bölüm için en çok 100 satır işlemi gönderilebilir; değişikliği bölün. ' +
      'Bu sınır kayıttaki satır sayısını sınırlamaz.',
  },
} as const

/**
 * The selected company account's active-state action and modal inside C-03.
 *
 * Every line says which state it is about: the COMPANY ACCOUNT's, never the shared
 * Party's and never an address's. The saved messages name the fresh read as the source
 * of what is on screen, because a 204 carries no body and the command is not the truth.
 */
export const CARI_ACCOUNT_STATUS_TEXT = {
  action: {
    deactivate: 'Bu şirkette pasife al',
    activate: 'Bu şirkette aktifleştir',
  },
  actionBlockedUnresolved: 'Sonucu belirsiz bir durum işlemi var; yeni bir işlem başlatmadan önce onu çözün.',
  title: {
    deactivate: 'Şirket hesabını pasife al',
    activate: 'Şirket hesabını aktifleştir',
  },
  scopeNote:
    'Yalnız seçili şirketteki hesabın durumu değişir. Ortak taraf kaydı, diğer şirket ' +
    'hesapları, adresler ve muhataplar etkilenmez.',
  target: 'Cari',
  company: 'Şirket',
  currentState: 'Mevcut hesap durumu',
  targetState: 'Yeni hesap durumu',
  active: 'Aktif',
  inactive: 'Pasif',
  reason: 'Gerekçe',
  reasonHint:
    'Zorunlu; boşluklar kırpıldıktan sonra 1–1000 karakter. Gizli banka veya kimlik bilgisi yazmayın.',
  reasonCount: (length: number) => `${length} / 1000`,
  reasonRequired: 'Gerekçe zorunludur.',
  reasonTooLong: (length: number) =>
    `Gerekçe boşluklar kırpıldıktan sonra en çok 1000 karakter olabilir (şu an ${length}).`,
  errorSummary: 'İşlem gönderilmedi; işaretli alanı düzeltin.',
  submit: {
    deactivate: 'Pasife al',
    activate: 'Aktifleştir',
  },
  submitting: 'Gönderiliyor…',
  cancel: 'Vazgeç',
  closeConfirmTitle: 'Gerekçe kaydedilmedi',
  closeConfirmDetail:
    'Pencereyi kapatırsanız yazdığınız gerekçe silinir. Sunucuya hiçbir şey gönderilmedi.',
  closeConfirmKeep: 'Yazmaya devam et',
  closeConfirmDiscard: 'Gerekçeyi bırak ve kapat',
  closeUnresolvedTitle: 'İşlemin sonucu belirsiz',
  closeUnresolvedDetail:
    'Pencereyi kapatmak işlemi iptal etmez ve geri almaz; sonuç yalnız aynı işlemi ' +
    'yeniden deneyerek ya da taze okumayla öğrenilir. İşlem uyarısı hesap bölümünde kalır.',
  closeUnresolvedKeep: 'Pencerede kal',
  closeUnresolvedClose: 'Pencereyi kapat',
  changedSinceOpen:
    'Kayıt bu pencere açıldıktan sonra güncellendi. Pencereyi kapatıp güncel kayıtla ' +
    'yeniden açın; eski sürümle gönderim yapılmaz.',
  savedTitle: {
    deactivate: 'Şirket hesabı pasife alındı',
    activate: 'Şirket hesabı aktifleştirildi',
  },
  savedDetail:
    'Güncel durum ve sürüm taze yetkili okumadan alındı; bu ekrandaki değerler o okumaya aittir.',
  savedReplayed:
    'Sunucu bu sonucu daha önce gönderilen aynı işlemin tekrarı olarak bildirdi; ikinci bir ' +
    'değişiklik yapılmadı.',
  savedListNote:
    'Cari Kartlar listesinde yalnız aktif hesaplar süzülüyorsa bu hesap orada görünmez; kayıt silinmedi.',
  readRecovery:
    'Kaydedildi, görünüm yenilenemedi. Yalnız okuma yeniden denenir; işlem ikinci kez gönderilmez.',
  readPending:
    'Kaydedildi; güncel kayıt henüz okunamadı. Ekrandaki değerler eski okumaya ait. Bağlantı yoksa okuma ' +
    'bağlantı gelince yapılır; işlem ikinci kez gönderilmez.',
  readAgain: 'Kaydı yeniden oku',
  contractNotReady:
    'Bu kayıt için uyumlu istemci teyidi alınmadı; durum işlemi açılmaz. Kaydı yeniden okuyun.',
  refreshRecord: 'Kaydı yenile',
  refreshing: 'Kayıt yenileniyor…',
  refreshedNote:
    'Kayıt güncel sürümle yenilendi; gerekçeniz korundu. Mevcut durumu inceleyip yeniden gönderin — ' +
    'bu yeni bir işlemdir.',
  adoptFresh: 'Güncel sürüme geç',
  changedWhileFrozen:
    'Kayıt bu pencere açıldıktan sonra güncellendi. Sonucu belirsiz işlem çözülene kadar burada ' +
    'gösterilen hedef ve sürüm, gönderilen işleminkiyle aynı kalır; yeni sürüme geçiş ancak kesin ' +
    'sonuç ya da işlemi bırakma sonrasında yapılır.',
  readAgainInPlace: 'Kaydı bu ekranda yeniden oku',
  contractRefusalRecovery:
    'Bekleyen işlem ve anahtarı bu ekranda korunuyor; sayfayı yenilemek onları kaybettirir. Uyumlu ' +
    'istemci sözleşmesi yeniden sağlandığında kaydı buradan yeniden okuyun; işlem otomatik tekrarlanmaz.',
  savedThenChangedTitle: 'İşlem uygulandı; kayıt sonrasında yeniden değişmiş',
  savedThenChangedDetail:
    'Sunucu bu işlemi uyguladı, ancak taze okuma hesabı farklı bir durumda gösteriyor — kayıt sizden ' +
    'sonra yeniden değiştirilmiş olabilir. Ekrandaki durum taze okumanındır; ikinci bir değişiklik ' +
    'gönderilmedi.',
  savedSupersededTitle: 'Geçmiş işlemin sonucu bildirildi',
  savedSupersededDetail:
    'Sunucu daha önce gönderilen işlemin tekrarını bildirdi; hesap o işlemden sonra yeniden değişmiş. ' +
    'Ekrandaki durum taze okumanındır; ikinci bir değişiklik gönderilmedi.',
  currentStateNote: (active: boolean) => `Güncel hesap durumu: ${active ? 'Aktif' : 'Pasif'}.`,
  replayedConflict:
    'Bu çatışma yanıtı daha önceki bir girişimin tekrarı; içindeki sürüm güncel sayılmaz. ' +
    'Güncel durum yalnız taze okumayla doğrulanır.',
  announceSaved: {
    deactivate: 'Şirket hesabı pasife alındı.',
    activate: 'Şirket hesabı aktifleştirildi.',
  },
  /** Where the shared command texts describe another command's situation. */
  failure: {
    'not-found': {
      title: 'Şirket hesabı bulunamadı',
      detail:
        'Seçili şirkette bu cariye ait bir hesap yok ya da görünmüyor; yeni hesap oluşturulmadı. ' +
        'Güncel durum yalnız yetkili okumayla doğrulanır.',
    },
    validation: {
      title: 'Sunucu doğrulaması başarısız',
      detail: 'Sunucu gerekçeyi veya sürümü kabul etmedi; yazdığınız gerekçe korundu.',
    },
  },
} as const

export const CARI_COMMAND_TEXT = {
  unknown: {
    title: 'İşlem sonucu belirsiz',
    detail:
      'Sunucudan kesin yanıt alınamadı; kayıt oluşmuş olabilir. Aynı işlemi yeniden denemek mükerrer kayıt oluşturmaz.',
    retry: 'Aynı işlemi yeniden dene',
    abandon: 'İşlemi bırak',
  },
  abandoned: {
    title: 'Bırakılan işlemin sonucu belirsiz',
    detail:
      'Aynı bilgilerle daha önce gönderilen işlemin sonucu belirsiz kaldı; kayıt oluşmuş olabilir. Mükerrer kayıt oluşmaması için aynı bilgilerle yeni bir işlem başlatılmaz: önceki işlemi yeniden deneyin ya da bilgileri değiştirin.',
    retry: 'Önceki işlemi yeniden dene',
  },
  unknownReason: {
    unacknowledged:
    'Sunucu bu işlemi onaylamadı; sonucu bilinmiyor. İşlem sunucuya ulaşmış olabilir. ' +
    'Aynı kaydı ikinci kez oluşturmayın — aşağıdaki “Aynı işlemi yeniden dene” ' +
    'düğmesini kullanın; aynı işlem anahtarı korunur.',
  offline: 'Bağlantı yok.',
    'network-error': 'Sunucuya ulaşılamadı.',
    'service-unavailable': 'Servis geçici olarak yanıt veremedi.',
    'malformed-response': 'Sunucu yanıtı beklenen biçimde değil.',
  } satisfies Record<AttemptUnknownReason, string>,
  failure: {
    validation: {
      title: 'Sunucu doğrulaması başarısız',
      detail: 'Alanları gözden geçirip yeniden gönderin.',
    },
    metadata: {
      title: 'İşlem bilgisi geçersiz',
      detail:
        'İstek üst bilgisi kabul edilmedi; işlem otomatik yinelenmedi. Formu düzenleyip yeni bir işlem başlatın.',
    },
    unauthenticated: {
      title: 'Oturum sonlandı',
      detail: 'Devam etmek için yeniden giriş yapın.',
    },
    forbidden: {
      title: 'Yetkiniz yok',
      detail: 'Bu işlem için yetkiniz bulunmuyor.',
    },
    'not-found': {
      title: 'Cari bulunamadı',
      detail: 'Seçilen cari bu hedef şirket için artık bağlanamıyor; aday listesi yenilendi.',
    },
    'already-linked': {
      title: 'Zaten bağlı olabilir',
      detail:
        'Bu cari hedef şirkette zaten bir hesaba sahip olabilir; aday listesi yenilendi. Güncel durum yalnız yetkili okumayla doğrulanır.',
    },
    'idempotency-conflict': {
      title: 'İşlem çakışması',
      detail: 'Aynı işlem anahtarı farklı bir istekle kullanılmış; işlem otomatik yinelenmedi.',
    },
    'version-conflict': {
      title: 'Kayıt siz düzenlerken değişti',
      detail:
        'Değişikliğiniz uygulanmadı; kayıt açtığınız sürümden sonra güncellenmiş. ' +
        'Aynı işlem yeniden denenmez: kaydı yenileyip güncel değerleri görün ve ' +
        'değişikliğinizi yeniden girin.',
    },
    unfenced: {
      title: 'Kaydın sürümü doğrulanamadı',
      detail:
        'Bu güncelleme kaydın okunduğu sürüme bağlanamadığı için gönderilmedi. ' +
        'Kaydı yenileyip düzenlemeyi tekrar açın.',
    },
    unexpected: {
      title: 'İşlem tamamlanamadı',
      detail: 'Sunucu isteği reddetti; iş etkisi oluşmadı.',
    },
    'contract-not-ready': {
      title: 'Uyumlu istemci gerekli',
      detail:
        'Bu cari işlemi desteklenen istemci sözleşmesi ile yeniden açılmalıdır. ' +
        'Sayfayı yenileyin; istek gönderilmedi.',
    },
  } satisfies Record<AttemptFailure, { title: string; detail: string }>,
  acknowledge: 'Anladım',
} as const

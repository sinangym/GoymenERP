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

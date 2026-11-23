import type { AiDataClass, AiEventType, AiFeature, AiPlacement, AiTaskKind } from './aiGatewayContract'

/** AY-YZG-01 "Yapay zekâ yönetimi" (WP-A5, AI-20) — the words of the AI management screen. */
export const AI_GATEWAY_TEXT = {
  title: 'Yapay zekâ yönetimi',
  intro:
    'Yapay zekâ özelliklerinin açık/kapalı durumu, görev × veri sınıfı model tablosu, aylık bütçe tavanı ve istek başına ayrılan birim tek bir sürümlü politikada tutulur. Her değişiklik yeni bir sürümdür; gerekçe zorunludur ve denetime yazılır. Bütçe değeri girilmeden hiçbir özellik açılamaz.',
  subject: 'Yapay zekâ politikası',
  // Fixed rules and data class decisions
  fixedHeading: 'Değiştirilemeyen kurallar',
  fixedRules: [
    'Bulut model en fazla "İç" sınıfındaki veriyi görebilir; "Gizli" ve "Çok gizli" veri şirket dışına çıkmaz (AI-D3, AI-D4).',
    'Belge ve e-posta metni yalnız şirket içi modelde okunur (AI-D24, AI-D27).',
    'Bütçe değeri (aylık tavan ve istek başı rezerv) girilmeden her özellik kapalı kalır (YOL-D21).',
    'Yapay zekâ çalışma ortamı (Ai:Enabled) ve sağlayıcı seçimi bu ekrandan açılmaz; insan kararıdır.',
  ],
  decisionsHeading: 'Veri sınıfı kararları',
  decisions: [
    'AI-D42 — Cari yetkili kişi adı, telefon, e-posta ve TCKN "Gizli" (yalnız şirket içi model); cari unvanı, bakiye ve tutarlar "İç".',
    'AI-D44 — Serbest panel sorusu varsayılan "İç"; kişisel veri (TCKN, telefon, IBAN vb.) içeriyorsa "Gizli"ye yükselir; kaynak veri daha yüksekse o sınıf geçerlidir.',
    'AI-D45 — Stok maliyeti yalnız maliyet görme izni olan kullanıcının asistanına açılır; "Gizli" sınıfındadır, bulut modele gitmez.',
  ],
  // Current policy
  currentHeading: 'Geçerli politika',
  version: 'Sürüm',
  notRecorded: 'Henüz politika kaydedilmedi: bütün özellikler kapalı, model tablosu boş, bütçe yok.',
  ceiling: 'Aylık kullanım tavanı',
  perRequest: 'İstek başına ayrılan birim',
  noBudget: 'Girilmedi (kapalı)',
  units: (value: number) => `${value.toLocaleString('tr-TR')} birim`,
  featuresHeading: 'Özellikler',
  open: 'Açık',
  closed: 'Kapalı',
  routesHeading: 'Görev × veri sınıfı model tablosu',
  routesEmpty: 'Model tablosu boş: hiçbir görev bir modele yönlendirilmez.',
  taskKind: 'Görev',
  noRoute: '—',
  // Usage
  usageHeading: (month: string) => `Kullanım ve kabul sayıları — ${month}`,
  usageSubject: 'Kullanım sayıları',
  committed: 'Ay toplamı',
  ceilingShare: (committed: number, ceiling: number) => `${committed.toLocaleString('tr-TR')} / ${ceiling.toLocaleString('tr-TR')} birim`,
  entries: 'İstek',
  openReservations: 'Açık rezerv',
  feature: 'Özellik',
  usageEmpty: 'Bu ay kayıtlı yapay zekâ isteği yok.',
  acceptanceHeading: 'Kabul sayıları',
  acceptanceNotMeasured: 'Kabul sayıları henüz ölçülmüyor (öğrenme kaydı WP-C9 ile gelir). Bu boşluk "kimse kabul etmedi" anlamına gelmez.',
  acceptanceEmpty: 'Bu ay kabul, düzeltme veya ret kaydı yok.',
  accepted: 'Kabul',
  corrected: 'Düzeltme',
  rejected: 'Ret',
  reverted: 'Sonradan geri alma',
  // Events
  eventsHeading: 'Yapay zekâ olayları',
  eventsSubject: 'Yapay zekâ olayları',
  eventsIntro: 'Güvenlik ve denetim kanalındaki yapay zekâ olayları salt okunur listelenir; okuma da güvenlik kanalına kaydedilir.',
  eventType: 'Olay türü',
  eventsEmpty: 'Bu türde olay yok.',
  when: 'Zaman',
  outcome: 'Sonuç',
  reasonCode: 'Neden kodu',
  tool: 'Araç',
  dataClass: 'Veri sınıfı',
  occurrences: 'Tekrar',
  correlation: 'İlişki kimliği',
  more: 'Daha eski olaylar',
  firstPage: 'İlk sayfaya dön',
  // History
  historyHeading: 'Değişiklik geçmişi',
  historyEmpty: 'Henüz sürüm kaydedilmedi.',
  featureCount: 'Açık özellik',
  routeCount: 'Model satırı',
  who: 'Kaydeden',
  recordedAt: 'Kayıt zamanı',
  reason: 'Gerekçe',
  // Editor
  edit: 'Yeni sürüm hazırla',
  editorHeading: 'Yeni politika sürümü',
  ceilingInput: (max: number) => `Aylık kullanım tavanı (birim, boş = kapalı, en fazla ${max.toLocaleString('tr-TR')})`,
  perRequestInput: (max: number) => `İstek başına ayrılan birim (boş = kapalı, 1–${max.toLocaleString('tr-TR')})`,
  budgetHint: 'Değer önerilmez; aylık bütçe insan kararıdır (S5). Tavan veya rezerv boşsa özellikler açılamaz.',
  featuresLocked: 'Bütçe değeri girilmeden özellik açılamaz.',
  addRoute: 'Model satırı ekle',
  removeRoute: (task: string, dataClass: string) => `${task} / ${dataClass} satırını kaldır`,
  placement: 'Yerleşim',
  modelDeployment: 'Model adı',
  cloudForbidden: 'Bulut bu veri sınıfında kullanılamaz.',
  duplicateRoute: 'Aynı görev ve veri sınıfı için tek satır girilebilir.',
  invalidCeiling: 'Tavan boş ya da 0 ile üst sınır arasında bir tam sayı olmalıdır.',
  invalidPerRequest: 'Rezerv boş ya da 1 ile üst sınır arasında bir tam sayı olmalıdır.',
  invalidRoute: 'Her satırda model adı zorunludur (en fazla 128 karakter).',
  preview: 'Değişiklik önizlemesi',
  effect: 'Yeni sürüm sunucuda kaydedilir; geçmiş sürümler değişmez. Ağ geçidi bir sonraki istekte yeni sürümü kullanır.',
  save: 'Kaydet',
  cancel: 'Vazgeç',
  pending: 'Kaydediliyor',
  retry: 'Aynı işlemi yeniden dene',
  unknown: 'Sonuç doğrulanamadı. Aynı anahtar ve içerikle yeniden deneyin.',
  refused: 'İşlem tamamlanamadı. Değerleri, kuralları, yetkiyi ve sürümü kontrol edin.',
  saved: 'Yeni sürüm kaydedildi.',
  replay: 'Önceki işlemin sonucu doğrulandı.',
  conflict: 'Politika değişti. Güncel sürüm yenilendi; değişikliği yeniden hazırlayın.',
} as const

export const AI_FEATURE_LABEL: Readonly<Record<AiFeature, string>> = {
  Panel: 'Ekran paneli',
  Summary: 'Onay kartı özeti',
  Document: 'Belgeden taslak',
  Query: 'Doğal dil sorgu',
  Write: 'Onaylı yazma önizlemesi',
  Chat: 'Genel sohbet',
  Scheduled: 'Zamanlı görevler ve özetler',
  Memory: 'Kişisel hafıza',
  Email: 'E-posta',
}

export const AI_TASK_KIND_LABEL: Readonly<Record<AiTaskKind, string>> = {
  Answer: 'Cevap',
  Summarize: 'Özet',
  Extract: 'Alan çıkarma',
  Classify: 'Sınıflandırma',
  Draft: 'Taslak',
  Converse: 'Sohbet',
}

export const AI_DATA_CLASS_LABEL: Readonly<Record<AiDataClass, string>> = {
  Public: 'Açık',
  Internal: 'İç',
  Confidential: 'Gizli',
  StrictlyConfidential: 'Çok gizli',
}

export const AI_PLACEMENT_LABEL: Readonly<Record<AiPlacement, string>> = {
  Cloud: 'Bulut',
  OnPremises: 'Şirket içi',
}

export const AI_EVENT_TYPE_LABEL: Readonly<Record<AiEventType, string>> = {
  'ai.request.refused': 'Reddedilen istekler',
  'ai.tool.called': 'Araç çağrıları',
  'ai.authz.denied': 'Yetki retleri',
}

export const budgetMonthLabel = (month: number) =>
  new Date(Date.UTC(Math.floor(month / 100), (month % 100) - 1, 1)).toLocaleDateString('tr-TR', { month: 'long', year: 'numeric', timeZone: 'UTC' })

/**
 * Every user-facing string of the Manufacturing order screen, in one place.
 *
 * Class and position names are DEC-PRDCT-006's own words (KILITLI): iyi ürün, ikinci
 * kalite, geri kazanılabilir artık, hurda, proses kaybı, WIP. The unexplained difference
 * is "açıklanamayan fark" and is never called fire (plan v2 §7.3).
 *
 * Refusals are chosen by the server's stable CODE; a code not listed here is shown as a
 * generic refusal WITH the code, never guessed into a nearby meaning.
 */

import type { FactFailure, FactUnknown } from './commandApi'
import type { FactKind, OrderStatus, OutputClass } from './contract'

export const OUTPUT_CLASS_LABEL: Record<OutputClass, string> = {
  GoodProduct: 'İyi ürün',
  SecondQuality: 'İkinci kalite',
  UsableRemnant: 'Geri kazanılabilir artık',
  Scrap: 'Hurda',
  ProcessLoss: 'Proses kaybı',
}

export const ORDER_STATUS_LABEL: Record<OrderStatus, string> = {
  Open: 'Açık',
  Closed: 'Kapalı',
}

export const TARGET_BASIS_LABEL: Record<string, string> = {
  InputMass: 'Girdi kütlesi',
  GoodOutputMass: 'İyi ürün kütlesi',
}

export const FACT_KIND_LABEL: Record<FactKind, string> = {
  'input-issues': 'Girdi çıkışı',
  'input-returns': 'Kullanılmayan girdi iadesi',
  'wip-measurements': 'WIP ölçümü',
  outputs: 'Üretim çıktısı',
}

export const FACT_KIND_HELP: Record<FactKind, string> = {
  'input-issues': 'Ana rulodan üretime verilen kütle.',
  'input-returns': 'Çıkıp kullanılmayan ve geri dönen kütle; çıkandan fazla olamaz.',
  'wip-measurements': 'Rulonun üzerindeki yarı işlenmiş kütlenin son okuması; öncekinden sonra olmalı.',
  outputs:
    'Yeni ürün etiketi ve stok hareketi aynı işlemde kaydedilir; sahiplik, bu emrin Inventory girdi çıkışından alınır.',
}

export const COST_STATUS_TEXT = 'Hesaplanmadı'

/** GDC-BAL/1 panel (BAL slice 2). The engine's input names, in the user's words; an unknown name is shown as it came. */
export const ENGINE_PARAMETER_LABEL: Record<string, string> = {
  fireKg: 'fire (kg)',
  rawMaterialKg: 'hammadde (kg)',
  actualKg: 'gerçek kg',
  actualCount: 'gerçek adet',
}

export function engineParameterLabel(name: string): string {
  return ENGINE_PARAMETER_LABEL[name] ?? name
}

export const MASS_BALANCE_HEADING = 'Kütle dengesi (GDC-BAL/1)'
export const MASS_BALANCE_HELP =
  'Emrin kapanışının kullandığı hesap: fiilî girdi − (iyi ürün + ikinci kalite + kullanılabilir artık + hurda + ' +
  'proses kaybı + kalan WIP + onaylı ölçüm düzeltmesi). Fark işaretiyle gösterilir ve hiçbir sınıfa yazılmaz.'
export const ENGINE_UNAVAILABLE = 'Hesaplanamıyor — eksik girdi:'
export const ENGINE_REFUSED = 'Hesap reddedildi:'
/** BAL-004 / BAL-006: why the order cannot supply their inputs yet (no number is guessed). */
export const SCRAP_PERCENT_NOTE =
  'Fire yüzdesinde hangi sınıfların fire sayılacağı ve hangi kütlenin taban olacağı iş kararı bekliyor.'
export const ACTUAL_UNIT_KG_NOTE = 'Fiilî birim kg için emirde adet kaydı yok; adet, adet üreten süreçlerle gelecek.'
export const REMAINING_UNAVAILABLE = 'Planda beyan edilmiş bir hedef yok; kalan hedef hesaplanmaz.'
export const KG_FORMAT_HELP = 'Nokta ile ondalık, işaretsiz (ör. 1250.5). Ondalık sınırı şirketin kg profilinden gelir.'
export const KG_FORMAT_ERROR = 'Kilogram biçimi geçersiz: işaretsiz, nokta ile ondalık bir sayı girin.'
export const ROLL_REQUIRED = 'Bir ana rulo seçin.'
export const TIME_REQUIRED = 'Olgunun zamanını girin.'
export const CLOSED_ORDER_TEXT = 'Emir kapalı; yeni gerçekleşme kaydedilmez.'

export const OUTPUT_INVENTORY_TEXT = {
  productRequired: 'Sunucunun doğruladığı bir stok ürünü seçin.',
  locationRequired: 'Sunucunun doğruladığı aktif bir lokasyon seçin.',
  classRequired: 'Çıktı sınıfını seçin.',
  labelRequired: 'Yeni çıktı etiket kodunu girin.',
  blockRequired: 'İlk stok durumunu açıkça seçin; ekran varsayılan seçmez.',
  productLoading: 'Ürün seçenekleri yükleniyor…',
  locationLoading: 'Lokasyon seçenekleri yükleniyor…',
  productUnavailable: 'Ürün seçenekleri okunamadı. Tekrar deneyin.',
  locationUnavailable: 'Lokasyon seçenekleri okunamadı. Tekrar deneyin.',
  issuedOwnershipHelp:
    'Çıktının sahip ve gözeten bilgisi, bu emrin ana rulo Inventory girdi çıkışından değişmeden alınır.',
  issuedOwnershipRequired:
    'Bu emir için ana rulonun tek ve eksiksiz Inventory girdi çıkışı sahipliği doğrulanmadan çıktı gönderilemez.',
  issuedOwnershipTruncated:
    'Ana rulonun hareket geçmişi bu görünümde kısaltılmış; bu emrin girdi çıkışı izi eksiksiz okunamadığı için ' +
    'çıktının sahipliği doğrulanamıyor. Çıktı gönderilemez.',
  issuedOwnershipHeading: 'Girdi çıkışından gelen sahiplik (değiştirilemez):',
  owner: 'Sahip',
  custodian: 'Gözeten',
  partyThisCompany: 'Bu şirket',
  partyCompany: 'Başka şirket',
  partyCounterparty: 'Cari',
} as const

export const INPUT_INVENTORY_TEXT = {
  heading: 'Ana rulo Inventory durumu',
  version: 'Etiket sürümü',
  physical: 'Fiziksel',
  free: 'Serbest',
  blocked: 'Blokeli',
  loadingRequired: 'Ana rulonun güncel Inventory durumu okunmadan bu işlem gönderilemez.',
} as const

/** MFG-02 order list. Column names are the summary's own fields; no figure is invented for a column. */
export const ORDER_LIST_TEXT = {
  heading: 'Üretim emirleri',
  caption: 'Şirketin üretim emirleri, en yeniden eskiye.',
  filter: 'Durum',
  filterAll: 'Hepsi',
  order: 'Emir (teknik referans)',
  createdAt: 'Oluşturma',
  status: 'Durum',
  process: 'Süreç',
  rollCount: 'Ana rulo',
  plannedInput: 'Planlanan girdi (kg)',
  target: 'Hedef',
  targetNone: 'Hedef yok',
  closedAt: 'Kapanış',
  open: 'Aç',
  loadMore: 'Sonraki sayfayı yükle',
  loadingMore: 'Yükleniyor…',
  nextPageFailed: 'Sonraki sayfa okunamadı; yukarıdaki satırlar yerinde kaldı.',
  end: 'Liste sonu.',
  labelNote: 'Rulo ve emir etiketleri ürün/etiket okumasıyla gelecek; burada teknik kimlik gösterilir.',
} as const

/**
 * MFG-ORD close. The rule is the decided one (MFG-4, Business 5728619644) in the user's words —
 * no tolerance is offered, no write-off is suggested and no new condition is introduced here.
 */
export const CLOSE_TEXT = {
  heading: 'Emri kapat',
  help:
    'Kapanış, her ana rulonun kendi kütlesini açıklamasını ister: bir rulodaki fazla, başka bir rulodaki eksiği ' +
    'kapatmaz. Kalan WIP emirde kendi pozisyonu olarak durur ve kapanışı engellemez. Açıklanamayan fark varsa emir ' +
    'kapanmaz; fark tolerans ile geçilmez ve hiçbir sınıfa yazılmaz.',
  action: 'Emri kapat',
  confirmQuestion: 'Emir kapatılsın mı? Kapanan emre yeni gerçekleşme yazılamaz.',
  confirm: 'Evet, emri kapat',
  cancel: 'Vazgeç',
  sending: 'Gönderiliyor…',
  resend: 'Aynı işlem anahtarıyla yeniden gönder',
  appliedTitle: 'Emir kapatıldı',
  appliedDetail: 'Emir kapatıldı; yukarıdaki değerler emrin yeniden okunmuş hâlidir.',
  replayedTitle: 'Emir zaten kapatılmıştı',
  replayedDetail: 'Bu kapanış sunucuda zaten kayıtlıydı; ikinci bir kapanış oluşmadı.',
  refusedTitle: 'Emir kapatılmadı',
  unknownTitle: 'Kapanışın sonucu bilinmiyor',
  unknownHelp:
    'Aynı kapanışı aynı işlem anahtarıyla yeniden gönderin: kapanmışsa ikinci bir kapanış oluşmaz.',
  closedAt: 'Kapanış zamanı',
} as const

export const APPLIED_TITLE = 'Kaydedildi'
export const APPLIED_DETAIL = 'Olgu kaydedildi. Tablodaki değerler emrin yeniden okunmuş hâlidir.'
export const REPLAYED_TITLE = 'Daha önce kaydedilmiş'
export const REPLAYED_DETAIL =
  'Bu işlem sunucuda zaten kayıtlıydı; ikinci bir kayıt oluşmadı. Tablo emrin güncel hâlidir.'

const RULE_TEXT: Record<string, string> = {
  manufacturing_order_closed: CLOSED_ORDER_TEXT,
  manufacturing_unexplained_difference:
    'Emir kapanmadı: en az bir ana ruloda açıklanamayan kütle farkı var. Fark tolerans ile geçilmez ve bir sınıfa ' +
    'yazılmaz; eksik kalan çıktı, iade ya da WIP kaydı tamamlandığında kapanış yeniden denenebilir.',
  manufacturing_roll_not_in_order: 'Bu ana rulo emirde yok.',
  manufacturing_roll_not_issued: 'Bu ana rulodan henüz girdi çıkışı yapılmadı.',
  manufacturing_input_already_issued: 'Bu olgu kimliğiyle bir girdi çıkışı zaten var.',
  manufacturing_return_already_recorded: 'Bu olgu kimliğiyle bir iade zaten var.',
  manufacturing_return_exceeds_issued: 'İade, bu rulodan çıkan kütleyi aşamaz.',
  manufacturing_output_already_declared: 'Bu olgu kimliğiyle bir çıktı zaten bildirildi.',
  manufacturing_wip_already_measured: 'Bu olgu kimliğiyle bir WIP ölçümü zaten var.',
  manufacturing_wip_measurement_not_latest: 'WIP ölçümü, rulonun son ölçümünden sonra olmalı.',
  manufacturing_mass_scale_exceeded: 'Kütle, şirketin kg profilinin izin verdiğinden fazla ondalık içeriyor; yuvarlanmadı.',
  manufacturing_kilogram_precision_exceeded: 'Kütle en fazla üç ondalık olabilir; yuvarlanmadı.',
  manufacturing_mass_negative: 'Kütle negatif olamaz.',
  manufacturing_value_does_not_fit: 'Kütle kaydedilebilir aralığın dışında.',
  manufacturing_kilogram_profile_not_in_effect: 'Olgu tarihinde geçerli bir kg profili yok.',
  manufacturing_kilogram_profile_invalid: 'Şirketin kg profili geçersiz.',
  manufacturing_kilogram_profile_company_mismatch: 'kg profili bu şirkete ait değil.',
  settings_unit_profile_not_found: 'Şirketin onaylı bir kg profili yok.',
  inventory_manufacturing_parent_label_not_releasable: 'Ana rulo üretime verilebilir durumda değil.',
  inventory_manufacturing_parent_label_context_conflict:
    'Ana rulo bu şirketin kendi malı ve kendi gözetiminde değil.',
  inventory_manufacturing_physical_stock_insufficient: 'Ana ruloda istenen kütle kadar serbest fiziksel stok yok.',
  inventory_manufacturing_parent_input_not_issued: 'Bu ana rulo için önce bir girdi çıkışı kaydedilmelidir.',
  inventory_manufacturing_input_return_exceeds_issued: 'İade, bu rulodan net çıkan kütleyi aşamaz.',
}

export function failureText(failure: FactFailure): string {
  switch (failure.kind) {
    case 'validation':
      return failure.field === null
        ? 'Sunucu isteği geçersiz buldu; değerleri kontrol edin.'
        : `Sunucu isteği geçersiz buldu (alan: ${failure.field}).`
    case 'unauthenticated':
      return 'Oturumunuz sona erdi; yeniden giriş yapın.'
    case 'forbidden':
      return 'Bu emre olgu kaydetme yetkiniz yok.'
    case 'not-found':
      return 'Emir bulunamadı.'
    case 'version-conflict':
      return 'Emir siz formu doldururken değişti. Güncel hâli yeniden okundu; değerleri kontrol edip yeniden gönderin.'
    case 'idempotency-conflict':
      return 'Bu işlem anahtarı başka bir istekle kullanılmış. Yeni bir gönderim olarak yeniden deneyin.'
    case 'concurrent-change':
      return 'Aynı anda başka bir kayıt işlendi; bu olgu kaydedilmedi. Emir yeniden okundu; yeniden gönderin.'
    case 'label-version-conflict':
      return 'Ana rulo etiketi siz formu doldururken değişti. Emir ve etiket yeniden okundu; değerleri kontrol edin.'
    case 'id-conflict':
      return `Bu olgu kimliği başka bir içerikle kayıtlı (${failure.code}). Yeni bir gönderim olarak yeniden deneyin.`
    case 'rule':
      return RULE_TEXT[failure.code] ?? `İşlem reddedildi (${failure.code}).`
    case 'unsupported-media':
      return 'İstek biçimi sunucu tarafından kabul edilmedi.'
  }
}

/**
 * The same refusals, told as a CLOSE. Only the sentences that would otherwise describe a fact are
 * replaced; every rule code still comes from {@link failureText}, so a code has one meaning.
 */
export function closeFailureText(failure: FactFailure): string {
  switch (failure.kind) {
    case 'forbidden':
      return 'Bu emri kapatma yetkiniz yok.'
    case 'version-conflict':
      return 'Emir siz bakarken değişti. Güncel hâli yeniden okundu; kapanışı yeniden değerlendirin.'
    case 'concurrent-change':
      return 'Aynı anda başka bir kayıt işlendi; emir kapatılmadı. Emir yeniden okundu; yeniden deneyin.'
    default:
      return failureText(failure)
  }
}

export function unknownText(unknown: FactUnknown): string {
  switch (unknown.kind) {
    case 'offline':
      return 'Bağlantı yok. Olgunun kaydedilip kaydedilmediği bilinmiyor.'
    case 'network-error':
      return 'Sunucudan cevap gelmedi. Olgunun kaydedilip kaydedilmediği bilinmiyor.'
    case 'service-unavailable':
      return `Sunucu şu an cevap veremedi (${unknown.status}). Olgunun kaydedilip kaydedilmediği bilinmiyor.`
    case 'unexpected-success':
      return `Sunucu beklenmeyen bir cevap verdi (${unknown.status}). Olgunun kaydedilip kaydedilmediği bilinmiyor.`
    case 'undecodable':
      return `Sunucunun cevabı okunamadı (${unknown.status}). Olgunun kaydedilip kaydedilmediği bilinmiyor.`
    case 'aborted':
      return 'İstek yarıda kaldı. Olgunun kaydedilip kaydedilmediği bilinmiyor.'
  }
}

export const UNKNOWN_ACTION_HELP =
  'Aynı olguyu aynı işlem anahtarıyla yeniden gönderin: kaydedildiyse ikinci bir kayıt oluşmaz. Yeni bir olgu, bu çözülene kadar gönderilemez.'

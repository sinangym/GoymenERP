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
import type { FactKind, OrderStatus, OutputClass, RemnantClassification, StockShortfallView } from './contract'
import { OUTPUT_PIECE_RULE_TEXT } from './outputPieceText'

export const OUTPUT_CLASS_LABEL: Record<OutputClass, string> = {
  GoodProduct: 'İyi ürün',
  SecondQuality: 'İkinci kalite',
  UsableRemnant: 'Geri kazanılabilir artık',
  Scrap: 'Hurda',
  ProcessLoss: 'Proses kaybı',
}

/** URT-S16 (BS-14): the label classifications a usable remnant may take. */
export const REMNANT_CLASSIFICATION_LABEL: Record<RemnantClassification, string> = {
  Standard: 'Standart',
  SecondQuality: 'İkinci kalite',
}

export const ORDER_STATUS_LABEL: Record<OrderStatus, string> = {
  Open: 'Açık',
  Closed: 'Kapalı',
  Cancelled: 'İptal',
}

export const TARGET_BASIS_LABEL: Record<string, string> = {
  InputMass: 'Girdi kütlesi',
  GoodOutputMass: 'İyi ürün kütlesi',
}

export const FACT_KIND_LABEL: Record<FactKind, string> = {
  'input-issues': 'Girdi çıkışı',
  'input-returns': 'Kullanılmayan girdi iadesi',
  'wip-measurements': 'WIP ölçümü',
  'process-losses': 'Stok dışı proses kaybı',
  outputs: 'Üretim çıktısı',
}

export const FACT_KIND_HELP: Record<FactKind, string> = {
  'input-issues': 'Ana rulodan üretime verilen kütle.',
  'input-returns': 'Çıkıp kullanılmayan ve geri dönen kütle; çıkandan fazla olamaz.',
  'wip-measurements': 'Rulonun üzerindeki yarı işlenmiş kütlenin son okuması; öncekinden sonra olmalı.',
  'process-losses':
    'Açıkça ölçülen stok dışı proses kaybı; Inventory etiketi veya stok hareketi oluşturmaz.',
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
export const MASS_BALANCE_TEXT = {
  subject: 'kütle dengesi',
  parentRoll: 'Ana rulo',
  actualInput: 'Fiilî girdi (kg)',
  accounted: 'Hesaplanan (kg)',
  difference: 'Kütle farkı (kg)',
  reconciliation: 'Mutabakat',
  scrapPercent: 'Fire %',
  actualUnitKg: 'Fiilî birim kg',
} as const
export const MASS_BALANCE_HELP =
  'Emrin kapanışının kullandığı hesap: fiilî girdi − (iyi ürün + ikinci kalite + kullanılabilir artık + hurda + ' +
  'proses kaybı + kalan WIP + onaylı ölçüm düzeltmesi). Fark işaretiyle gösterilir ve hiçbir sınıfa yazılmaz.'
export const ENGINE_UNAVAILABLE = 'Hesaplanamıyor — eksik girdi:'
export const ENGINE_REFUSED = 'Hesap reddedildi:'
/** BAL-004 / BAL-006: why the order cannot supply their inputs yet (no number is guessed). */
export const SCRAP_PERCENT_NOTE =
  'Fire yüzdesi, beyan edilmiş hurda ile stok dışı proses kaybının toplamını çıkıştan iade düşülmüş hammaddeye böler. Açıklanamayan fark fire sayılmaz.'
export const ACTUAL_UNIT_KG_NOTE = 'Fiilî birim kg için emirde adet kaydı yok; adet, adet üreten süreçlerle gelecek.'
export const REMAINING_UNAVAILABLE = 'Planda beyan edilmiş bir hedef yok; kalan hedef hesaplanmaz.'
export const KG_FORMAT_HELP = 'Nokta ile ondalık, işaretsiz (ör. 1250.5). Ondalık sınırı şirketin kg profilinden gelir.'
export const KG_FORMAT_ERROR = 'Kilogram biçimi geçersiz: işaretsiz, nokta ile ondalık bir sayı girin.'
export const ROLL_REQUIRED = 'Bir ana rulo seçin.'
export const TIME_REQUIRED = 'Olgunun zamanını girin.'
export const CANCELLED_ORDER_TEXT = 'Emir iptal edildi; yeni gerçekleşme kaydedilmez.'
export const CLOSED_ORDER_TEXT = 'Emir kapalı; yeni gerçekleşme kaydedilmez.'

export const OUTPUT_INVENTORY_TEXT = {
  productRequired: 'Sunucunun doğruladığı bir stok ürünü seçin.',
  locationRequired: 'Sunucunun doğruladığı aktif bir lokasyon seçin.',
  classRequired: 'Çıktı sınıfını seçin.',
  outputLine: 'Plan çıktı satırı',
  outputLineRequired: 'Beyanın bağlı olduğu plan çıktı satırını seçin.',
  outputLineLabel: (kind: string, sequence: number | null, lineId: string | null) =>
    kind === 'SlitLine' ? `Dilme satırı ${sequence}` : kind === 'CutLine' ? `Kesim satırı ${lineId}` : 'Çıktı sınıfı satırı',
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
  heading: 'Üretim çıktısı etiketi',
  outputClass: 'Çıktı sınıfı',
  remnantClassification: 'Artık etiket sınıfı',
  remnantClassificationHelp:
    'Geri kazanılabilir artık, seçilen sınıfla etiketlenir ve ayrıca "geri kazanılabilir artık" üretim durumunu taşır. ' +
    'Varsayılan ikinci kalitedir.',
  productSearch: 'Ürün ara',
  product: 'Ürün',
  effectiveOnRequired: 'Önce geçerli çıktı zamanını girin.',
  moreMatches: 'Daha fazla eşleşme var; aramayı daraltın.',
  locationSearch: 'Lokasyon ara',
  location: 'Lokasyon',
  labelCode: 'Yeni etiket kodu (opsiyonel)',
  labelCodeHelp:
    "Boş bırakılırsa çıktı etiketinin numarası Ayarlar'daki çıktı etiket biçiminden verilir. Girdi rulosunun " +
    'numarası çıktı etiketine yazılmaz; girdi yalnız soy bağı olarak kalır.',
  heatNumber: 'Döküm no (opsiyonel)',
  coilNumber: 'Rulo no (opsiyonel)',
  documents: 'Belge referansları (satır başına)',
  initialBlock: 'İlk stok durumu',
  blockFree: 'Serbest',
  blockBlocked: 'Blokeli',
} as const

export const INPUT_INVENTORY_TEXT = {
  heading: 'Ana rulo Inventory durumu',
  subject: 'ana rulo Inventory durumu',
  label: 'Etiket',
  version: 'Etiket sürümü',
  physical: 'Fiziksel',
  free: 'Serbest',
  blocked: 'Blokeli',
  loadingRequired: 'Ana rulonun güncel Inventory durumu okunmadan bu işlem gönderilemez.',
} as const

/** The order screen's own frame: its summary, the roll balance table and the fact form's general words. */
export const ORDER_DETAIL_TEXT = {
  heading: 'Üretim emri',
  subject: 'üretim emri',
  contextSubject: 'üretim emri için tek şirket bağlamı',
  outstandingTitle: 'Sonucu bilinmeyen bir olgunuz var',
  outstandingDetail:
    'Gönderdiğiniz olgunun sonucu hâlâ belirsiz ve emir şu an okunamıyor. Olgu kendiliğinden tekrarlanmadı. ' +
    'Emir yeniden okunduğunda aynı işlem anahtarıyla devam edebilirsiniz.',
  summary: 'Emir özeti',
  order: 'Emir',
  status: 'Durum',
  plan: 'Plan',
  workCenter: 'İş merkezi',
  process: 'Proses',
  target: 'Hedef',
  targetUndeclared: 'Beyan edilmedi',
  remainingTarget: 'Kalan hedef',
  remaining: (remainingKg: string | null, achievedKg: string | null, targetKg: string | null) =>
    `${remainingKg} kg (gerçekleşen ${achievedKg} / ${targetKg} kg)`,
  cost: 'Maliyet',
  rollsHeading: 'Ana rulo dengesi (kg)',
  rollsHelp: 'Her rulo kendi başına mutabık olur: çıkan − iade = çıktılar + WIP + açıklanamayan fark.',
  parentRoll: 'Ana rulo',
  planned: 'Planlanan',
  issued: 'Çıkan',
  returned: 'İade',
  netIssued: 'Net çıkan',
  workInProgress: 'WIP',
  unexplained: 'Açıklanamayan fark',
  reconciliation: 'Mutabakat',
  balanced: 'Mutabık',
  different: 'Fark var',
} as const

export const FACT_FORM_TEXT = {
  heading: 'Gerçekleşme kaydı',
  kind: 'Olgu türü',
  parentRoll: 'Ana rulo',
  choose: 'Seçin',
  kg: 'Kütle (kg)',
  time: 'Zaman',
  resend: 'Aynı olguyu yeniden gönder',
  sending: 'Gönderiliyor…',
  save: 'Kaydet',
  refreshing: 'Emir yeniden okunuyor…',
  failedTitle: 'Kaydedilmedi',
  unknownTitle: 'Sonuç bilinmiyor',
  lastRefusal: (refusal: string) => `Son yeniden gönderim reddedildi: ${refusal}`,
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
  plans: 'Üretim planları',
  scrapPrices: 'Aylık hurda fiyatları',
  newPlan: 'Yeni üretim planı',
  subject: 'üretim emirleri',
  contextSubject: 'üretim emirleri için tek şirket bağlamı',
  nextPageSubject: 'sonraki sayfa',
} as const

/**
 * MFG-ORD close. Fire remains explicit Scrap + ProcessLoss and the server selects the exact Settings
 * tolerance snapshot. An out-of-tolerance close is a separate, reasoned authority; this text never
 * invents a percentage or promotes an unexplained difference to fire.
 */
export const CLOSE_TEXT = {
  heading: 'Emri kapat',
  help:
    'Kapanış, her ana rulonun kendi kütlesini açıklamasını ister: bir rulodaki fazla, başka bir rulodaki eksiği ' +
    'kapatmaz. Kalan WIP emirde kendi pozisyonu olarak durur ve kapanışı engellemez. Fire yalnız kaydedilmiş hurda ve ' +
    'proses kaybıdır; sunucu geçerli şirket/proses toleransını seçer. Açıklanamayan fark tolerans ile geçilmez.',
  action: 'Emri kapat',
  confirmQuestion: 'Emir kapatılsın mı? Kapanan emre yeni gerçekleşme yazılamaz.',
  confirm: 'Evet, emri kapat',
  cancel: 'Vazgeç',
  exceptionHeading: 'Tolerans dışı kapanış onayı',
  exceptionHelp:
    'Sunucu bu kapanış için ayrı onay istedi. Gerekçe, okuduğunuz emir sürümü ve seçilen tolerans profiliyle birlikte kaydedilir.',
  exceptionReason: 'Onay gerekçesi',
  exceptionReasonRequired: 'Onay gerekçesi zorunludur.',
  exceptionConfirm: 'Gerekçeyle onayla ve kapat',
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

/**
 * F-009 D6 (UI-14, API-25): the output correction form. A correction is an append-only reversal of one recorded output
 * declaration: the declaration is never deleted, and Inventory records its own reversal movement in the same
 * transaction. The text never promises a stock figure; the server decides under the label's fence.
 */
export const OUTPUT_CORRECTION_TEXT = {
  heading: 'Çıktı düzeltme',
  help:
    'Kaydedilmiş bir çıktı beyanından kütle geri alınır. Beyan silinmez; ters kayıt beyana bağlı yeni bir olgu olarak ' +
    'yazılır ve stok aynı işlemde düzeltilir. Sevk edilmiş, aktarılmış, ayrılmış veya başka üretime verilmiş kütle ' +
    'geri alınamaz.',
  outputsLoading: 'Emrin çıktıları okunuyor…',
  outputsSubject: 'emrin çıktıları',
  noOutputs: 'Bu emirde düzeltilecek bir çıktı beyanı yok.',
  truncated: 'Çıktı listesi sunucunun okuma sınırına ulaştı; aradığınız çıktı listede olmayabilir.',
  output: 'Düzeltilecek çıktı',
  outputPlaceholder: 'Bir çıktı seçin',
  outputOption: (outputId: string, labelId: string) => `Çıktı ${outputId} · etiket ${labelId}`,
  outputRequired: 'Düzeltilecek çıktıyı seçin.',
  labelSubject: 'çıktı etiketi',
  labelLoading: 'Çıktı etiketi okunuyor…',
  labelVersion: 'Etiket sürümü',
  labelNotReady: 'Çıktı etiketi okunmadan düzeltme gönderilemez.',
  labelPermission: 'Çıktı etiketini okuma yetkiniz yok; etiket sürümü olmadan düzeltme gönderilemez.',
  kg: 'Geri alınacak kg',
  reason: 'Düzeltme gerekçesi',
  reasonRequired: 'Düzeltme gerekçesi zorunludur.',
  occurredAt: 'Düzeltme zamanı',
  closedOrder: 'Emir açık değil; kapalı emirde çıktı düzeltmesi yapılmaz.',
  submit: 'Düzeltmeyi gönder',
  confirmQuestion: 'Seçilen çıktıdan bu kütle geri alınsın mı? Ters kayıt silinemez; yalnız yeni bir olguyla dengelenir.',
  confirm: 'Evet, düzeltmeyi kaydet',
  cancel: 'Vazgeç',
  sending: 'Gönderiliyor…',
  resend: 'Aynı işlem anahtarıyla yeniden gönder',
  appliedTitle: 'Düzeltme kaydedildi',
  appliedDetail: 'Ters kayıt yazıldı; yukarıdaki değerler emrin yeniden okunmuş hâlidir.',
  replayedTitle: 'Düzeltme zaten kayıtlıydı',
  replayedDetail: 'Bu düzeltme sunucuda zaten kayıtlıydı; ikinci bir ters kayıt oluşmadı.',
  refusedTitle: 'Düzeltme kaydedilmedi',
  unknownTitle: 'Düzeltmenin sonucu bilinmiyor',
  unknownHelp:
    'Aynı düzeltmeyi aynı işlem anahtarıyla yeniden gönderin: kaydedildiyse ikinci bir ters kayıt oluşmaz. Yeni bir ' +
    'düzeltme, bu çözülene kadar gönderilemez.',
} as const

/**
 * F-009 D5/D6 (URT-D33): the order cancel. The order and its facts are not deleted; a cancelled order is a status, and
 * the plan returns to Approved on the server. The text names the server's preconditions; it never checks them itself.
 */
export const ORDER_CANCEL_TEXT = {
  heading: 'Emri iptal et',
  help:
    'İptal, yalnız emirde hiçbir şey kalmadığında yapılır: net çıktı ve net girdi çıkışı sıfır, yarı mamul yok, ' +
    'kilitli kütle yok. Emir ve olguları silinmez; plan yeniden onaylı duruma döner. Koşulları sunucu denetler.',
  action: 'Emri iptal et',
  reason: 'İptal gerekçesi',
  reasonRequired: 'İptal gerekçesi zorunludur.',
  confirmQuestion: 'Emir iptal edilsin mi? İptal edilen emre yeni gerçekleşme yazılamaz.',
  confirm: 'Evet, emri iptal et',
  cancel: 'Vazgeç',
  notOpen: 'Emir açık değil; iptal edilemez.',
  sending: 'Gönderiliyor…',
  resend: 'Aynı işlem anahtarıyla yeniden gönder',
  appliedTitle: 'Emir iptal edildi',
  appliedDetail: 'Emir iptal edildi; yukarıdaki değerler emrin yeniden okunmuş hâlidir.',
  replayedTitle: 'Emir zaten iptal edilmişti',
  replayedDetail: 'Bu iptal sunucuda zaten kayıtlıydı; ikinci bir iptal oluşmadı.',
  refusedTitle: 'Emir iptal edilmedi',
  unknownTitle: 'İptalin sonucu bilinmiyor',
  unknownHelp: 'Aynı iptali aynı işlem anahtarıyla yeniden gönderin: iptal edildiyse ikinci bir iptal oluşmaz.',
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
  manufacturing_closure_fire_declaration_required:
    'Emir kapanmadı: açıklanamayan pozitif fark önce hurda veya stok dışı proses kaybı olarak açıkça kaydedilmelidir.',
  manufacturing_closure_approval_required:
    'Kaydedilmiş fire seçilen toleransın dışında; kapanış ayrı yetki ve gerekçeli onay istiyor.',
  manufacturing_closure_policy_not_effective:
    'Kapanış tarihinde etkin bir üretim tolerans profili yok.',
  manufacturing_closure_policy_scope_mismatch:
    'Seçilen üretim tolerans profili bu şirket veya prosese ait değil.',
  manufacturing_closure_policy_invalid:
    'Üretim tolerans profili geçersiz olduğu için emir kapatılamadı.',
  manufacturing_closure_approval_invalid:
    'Kapanış onayı güncel şirket, emir, sürüm veya tolerans profiliyle eşleşmiyor.',
  manufacturing_closure_approval_unexpected:
    'Bu kapanış tolerans içinde; istisna onayı gönderilmemelidir.',
  // URT-D41/URT-D42 (SINAN_KARARLARI_2026-11-13): output above input never closes; no tolerance and no approval path.
  manufacturing_closure_output_exceeds_input:
    'Emir kapanmadı: en az bir ana ruloda çıkan kg girenden fazla. Bu yönde tolerans ve onay yolu yoktur; o rulonun ' +
    'giriş ve çıkış kayıtları kontrol edilmelidir.',
  manufacturing_roll_not_in_order: 'Bu ana rulo emirde yok.',
  manufacturing_roll_not_issued: 'Bu ana rulodan henüz girdi çıkışı yapılmadı.',
  manufacturing_input_already_issued: 'Bu olgu kimliğiyle bir girdi çıkışı zaten var.',
  manufacturing_return_already_recorded: 'Bu olgu kimliğiyle bir iade zaten var.',
  manufacturing_return_exceeds_issued: 'İade, bu rulodan çıkan kütleyi aşamaz.',
  // F-009 D4: the return is bounded by the input still unconsumed (issued − returned − net output − WIP).
  manufacturing_return_exceeds_unconsumed:
    'İade, net çıktı ve yarı mamul düşüldükten sonra kalan girdi kütlesini aşamaz. Önce çıktıyı düzeltin veya ' +
    'yarı mamulü yeniden ölçün.',
  manufacturing_output_already_declared: 'Bu olgu kimliğiyle bir çıktı zaten bildirildi.',
  // F-009 D2/D6 (URT-D28, URT-D30): the output reversal's own domain refusals. Nothing is deleted; a reversal is a new
  // fact linked to the declaration, and its sum never exceeds the declared mass.
  manufacturing_output_not_declared: 'Düzeltilmek istenen çıktı bu emirde beyan edilmedi.',
  manufacturing_output_line_missing:
    'Bu çıktı beyanı bir plan çıktı satırına bağlı değil (satır referansı gelmeden kaydedilmiş). Satır sınırı ' +
    'hesaplanamadığı için düzeltilemez; emir bugünkü kapanış kurallarıyla kapatılır.',
  manufacturing_output_line_not_in_plan:
    'Çıktının bağlandığı satır bu emrin plan çıktı satırlarından biri değil ya da çıktı sınıfına uymuyor.',
  manufacturing_output_reversal_already_recorded: 'Bu kimlikle bir çıktı düzeltmesi zaten kaydedildi.',
  manufacturing_output_reversal_exceeds_declared:
    'Düzeltme, bu çıktı beyanının kalan net kütlesini aşıyor; bir beyanın ters kayıtlarının toplamı beyan edilen ' +
    'kütleyi geçemez.',
  // AK-7 (URT-D112): the piece side of an output declaration and its correction.
  ...OUTPUT_PIECE_RULE_TEXT,
  // F-009 D3/D6 (API-25): the physical reason Inventory gave, under its label fence, for not taking the mass back.
  manufacturing_output_correction_exceeds_produced:
    'Düzeltme, bu çıktı etiketine üretimden giren kütleyi aşıyor; etiketteki üretim kaydından fazlası geri alınamaz.',
  manufacturing_output_correction_shipped:
    'Bu çıktı etiketinden sevkiyat yapılmış; sevk edilmiş kütle üretim düzeltmesiyle geri alınamaz.',
  manufacturing_output_correction_issued_to_manufacturing:
    'Bu çıktı etiketi başka bir üretime girdi olarak verilmiş; önce o çıkış iade edilmelidir.',
  manufacturing_output_correction_transferred:
    'Bu çıktı etiketinin stoğu başka bir konuma veya gözetime aktarılmış; düzeltme yapılamaz.',
  manufacturing_output_correction_allocated:
    'Bu çıktı etiketinde ayrılmış (rezerve) kütle var; ayırma kaldırılmadan düzeltme yapılamaz.',
  manufacturing_output_correction_blocked: 'Bu çıktı etiketi blokeli; bloke kaldırılmadan düzeltme yapılamaz.',
  manufacturing_output_correction_not_free:
    'Bu çıktı etiketinde düzeltme kadar serbest stok yok; kütle başka bir işlemde kullanılıyor.',
  manufacturing_output_correction_label_unreadable:
    'Çıktı etiketinin durumu okunamadı; düzeltme kaydedilmedi. Biraz sonra yeniden deneyin.',
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

/**
 * SAT-D318 (owner decision of 2026-11-13): the input issue's physical-stock refusal, told with what the server named —
 * the refused issue, its parent label, the place of the label's stock, and, by SINAN-CEVAP (8) question 5 on Issue
 * #66, the usable and the requested kilograms together. The usable kilograms are the server's figure, shown as sent;
 * reserved kilograms are not part of the refusal, so the sentence never states or guesses one. The ids are shown as
 * the server sent them.
 */
export function stockShortfallText(shortfall: StockShortfallView): string {
  const place =
    shortfall.locationId === null
      ? 'Konum: yok; etiketin şu an hiçbir konumda stoğu bulunmuyor.'
      : `Konum: ${shortfall.locationId}.`
  return [
    'Girdi çıkışı kaydedilmedi: fiziksel stok sıfırın altına düşürülemez.',
    `İşlem: girdi çıkışı (olgu ${shortfall.sourceLineId}).`,
    `Ana rulo etiketi: ${shortfall.labelId}.`,
    place,
    `Etikette kullanılabilir ${shortfall.availableKg} kg var, ${shortfall.requestedKg} kg istendi.`,
  ].join(' ')
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
      if (failure.stockShortfall !== undefined) return stockShortfallText(failure.stockShortfall)
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

/** The same refusals, told as an OUTPUT CORRECTION; every rule code still comes from {@link failureText}. */
export function correctionFailureText(failure: FactFailure): string {
  switch (failure.kind) {
    case 'forbidden':
      return 'Bu emirde çıktı düzeltme yetkiniz yok.'
    case 'not-found':
      return 'Emir, çıktı beyanı veya çıktı etiketi bulunamadı.'
    case 'version-conflict':
      return 'Emir siz formu doldururken değişti. Güncel hâli yeniden okundu; çıktıyı ve kütleyi kontrol edip yeniden gönderin.'
    case 'concurrent-change':
      return 'Aynı anda başka bir kayıt işlendi; düzeltme kaydedilmedi. Emir yeniden okundu; yeniden gönderin.'
    case 'label-version-conflict':
      return 'Çıktı etiketi siz formu doldururken değişti. Etiket yeniden okundu; değerleri kontrol edip yeniden gönderin.'
    case 'id-conflict':
      return `Bu düzeltme kimliği başka bir içerikle kayıtlı (${failure.code}). Yeni bir gönderim olarak yeniden deneyin.`
    default:
      return failureText(failure)
  }
}

/** The same refusals, told as an ORDER CANCEL; every rule code still comes from {@link failureText}. */
export function cancelFailureText(failure: FactFailure): string {
  switch (failure.kind) {
    case 'forbidden':
      return 'Bu emri iptal etme yetkiniz yok.'
    case 'version-conflict':
      return 'Emir siz bakarken değişti. Güncel hâli yeniden okundu; iptali yeniden değerlendirin.'
    case 'concurrent-change':
      return 'Aynı anda başka bir kayıt işlendi; emir iptal edilmedi. Emir yeniden okundu; yeniden deneyin.'
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


export const PLAN_REAPPROVAL_TEXT = {
  heading: 'İptal sonrası planı yeniden onayla',
  help: 'Ayrılacak her etiketi açıkça seçin. Bu adım aynı girdilerle yeniden ayırır; farklı etiket için plan yeniden hesaplanmalıdır.',
  chooseAll: 'Her plan girdisinin etiketi açıkça seçilmelidir.',
  submit: 'Seçilen etiketlerle yeniden onayla',
  resend: 'Aynı isteği tekrar gönder',
  pending: 'Yeniden onay sonucu bilinmiyor. Aynı istek ve anahtarla tekrar deneyin.',
  applied: 'Plan yeniden onaylandı; güncel plan okunuyor.',
  refused: 'Yeniden onay reddedildi.',
  changedSettings: 'Ayarlar ilk onaydan bu yana değişti; plan iptal edilip yeni plan hazırlanmalıdır.',
  changedLabels: 'Bu yeniden onay adımı yalnız plandaki etiketleri kabul eder; farklı etiket planın yeniden hesabını gerektirir.',
  history: 'Yeniden onay geçmişi',
  historyLine: (number: number, at: string, version: number) => `${number}. yeniden onay: ${at}, plan sürümü ${version}`,
} as const

export const PLAN_INPUT_AVAILABILITY_TEXT = {
  heading: 'Girdi miktarları ve iptal geçmişi', subject: 'Plan girdilerinin güncel miktarları',
  help: 'Bu okuma stok ayırmaz. Serbest miktar başka işlemlerle değişebilir; yeniden onayda bütün girdiler birlikte tekrar kontrol edilir. Fiziksel bakiye serbest miktar değildir.',
  caption: 'Stok kaynağından okunan miktarlar (kg)', label: 'Etiket', product: 'Ürün', version: 'Güncel etiket sürümü',
  requested: 'Planlanan', physical: 'Fiziksel', blocked: 'Blokeli', allocated: 'Ayrılmış', free: 'Serbest', missing: 'Eksik',
  positions: 'Serbest pozisyon sayısı', positionRefusal: 'Ayırma için tek serbest pozisyon gerekir',
  readAt: 'Okuma zamanı', refresh: 'Miktarları yeniden oku', cancelledHistory: 'İptal edilen emir geçmişi',
  cancelledLine: (orderId: string, version: number) => `İptal edilen emir ${orderId}; iptal sonrası plan sürümü ${version}`,
} as const
export const PLAN_QUEUE_TEXT = {
  heading: 'İş merkezi kuyruğu ve plan önceliği', subject: 'İş merkezi kuyruğu',
  help: 'Sıra: önce termin, sonra yüksek öncelik puanı, sonra oluşturulma zamanı. Taslak satırı yalnız önizlemedir. Kalan miktar ve plan aşımı sunucudan gelir.',
  caption: 'İş merkezinin kayıtlı kuyruğu', plan: 'Plan', due: 'Termin', priority: 'Öncelik', planned: 'Planlanan kg',
  processed: 'İşlenen kg', remaining: 'Kalan kg', status: 'Durum', target: 'Bu plan', noDueDate: 'Termin yok',
  queued: 'Kuyrukta', preview: 'Önizleme', overPlanned: 'Planı aştı', currentPriority: 'Güncel öncelik puanı', revision: 'Revizyon',
  score: 'Yeni öncelik puanı', reason: 'Değişiklik gerekçesi', save: 'Önceliği kaydet', resend: 'Aynı denemenin sonucunu sor', refresh: 'Kuyruğu yenile',
  invalid: 'Tam sayı öncelik puanı ve gerekçe girin.', unknown: 'Kaydın sonucu bilinmiyor. Yeni deneme açmadan aynı anahtarla sonucunu sorun.',
  conflict: 'Plan veya öncelik değişti. Güncel kaydı okuyup yeni bir deneme hazırlayın.', refused: 'Öncelik kaydedilemedi.',
  applied: 'Öncelik kaydedildi; güncel kuyruk yeniden okunuyor.',
} as const

export const PLAN_ALLOCATION_SHORTFALL_TEXT = {
  heading: 'Stok ayırma reddedildi',
  help: 'Bu miktarlar reddedilen işlemin anına aittir. Ayrılabilir ve eksik miktarı stok kaynağı bildirdi; güncel girdi okuması bu red anının yerine geçmez.',
  product: 'Ürün', label: 'Etiket', version: 'Red anındaki etiket sürümü', requested: 'İstenen kg', available: 'Ayrılabilir kg', missing: 'Eksik kg',
} as const

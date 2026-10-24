/**
 * Accounting user texts (Turkish). Kept in one file so they are localizable; no server prose is
 * shown to the user — the client branches on stable codes and statuses only.
 */

import type { PeriodFailure, PeriodUnknown } from './commandApi'
import type { JournalKind, PeriodStatus } from './contract'
import type { JournalReversalFailure } from './journalReversalApi'
import type { NormalJournalFailure } from './normalJournalApi'

export const PERIODS_TITLE = 'Muhasebe Dönemleri'
export const PERIODS_INTRO =
  'Bir dönem, onu hazırlayandan farklı bir yetkili onaylayana kadar açılmaz ve kayıt almaz. Kapanmış bir döneme yeni kayıt girmez; düzeltme açık bir dönemde yeni kayıtla yapılır.'
export const PROPOSE_LINK = 'Yeni dönem öner'
export const MORE = 'Daha fazla'

export const PERIOD_STATUS_LABEL: Readonly<Record<PeriodStatus, string>> = {
  Proposed: 'Önerildi — onay bekliyor',
  Open: 'Açık',
  Closed: 'Kapalı',
}

export const OWN_PROPOSAL = 'Sizin öneriniz — başka bir yetkili onaylamalı'
export const APPROVE = 'Onayla'
export const APPROVE_CONFIRM_TEXT =
  'Onaydan sonra dönem açılır ve kayıt alır. Onay tektir; dönemi öneren kişi onaylayamaz.'
export const APPROVE_CONFIRM = 'Evet, onayla'
export const CLOSE = 'Kapat'
export const CLOSE_CONFIRM_TEXT = 'Kapanan döneme yeni kayıt girmez. Kapanış geri alınamaz; düzeltme açık bir dönemde yeni kayıtla yapılır.'
export const CLOSE_CONFIRM = 'Evet, kapat'
export const CANCEL = 'Vazgeç'
export const APPROVED_NOTICE = 'Dönem açıldı. Liste sunucudan yeniden okundu.'
export const CLOSED_NOTICE = 'Dönem kapandı. Liste sunucudan yeniden okundu.'
export const REPLAYED_NOTICE = 'Bu işlem daha önce kaydedilmişti; sunucunun ilk cevabı gösterildi. Liste yeniden okundu.'
export const JOURNALS_OF_PERIOD = 'Fişleri'

export const PROPOSE_TITLE = 'Yeni dönem öner'
export const PROPOSE_INTRO =
  'Öneri kaydedilir ancak başka bir yetkili onaylayana kadar açılmaz. Şirketin başka bir dönemiyle çakışan tarih kabul edilmez.'
export const STARTS_ON = 'Başlangıç'
export const ENDS_ON = 'Bitiş'
export const PROPOSE_SUBMIT = 'Öner'
export const PROPOSED_NOTICE = 'Dönem önerildi; açılması için başka bir yetkili onaylamalı.'
export const BACK_TO_PERIODS = 'Dönemlere dön'
export const DATE_REQUIRED = 'Başlangıç ve bitiş tarihini girin.'
export const DATE_ORDER = 'Bitiş tarihi başlangıçtan önce olamaz.'

export const JOURNALS_TITLE = 'Muhasebe Fişleri'
export const JOURNALS_INTRO =
  'Fişler kaynak kayıtlardan türetilir ve burada yalnız okunur. Bir fiş ikinci bir para hareketi değildir.'
export const JOURNAL_KIND_LABEL: Readonly<Record<JournalKind, string>> = {
  Original: 'Asıl',
  Reversal: 'Ters kayıt',
  Correction: 'Düzeltme',
}
export const OPEN_JOURNAL = 'Aç'
export const JOURNAL_DETAIL_TITLE = 'Muhasebe fişi'
export const DERIVED_NOTE = 'Bu fiş bir kaynak kaydının türev muhasebe kaydıdır; ikinci bir para hareketi değildir.'
export const PERIOD_FILTER_NOTE = 'Yalnız bir dönemin fişleri gösteriliyor.'
export const ALL_JOURNALS = 'Tüm fişler'

// Technical references are never presented as names (Business 5745076917). The chart of accounts is an open
// decision (NEEDS-BUSINESS 5740501573), so a journal line carries an account REFERENCE with no account name;
// a source record's name belongs to its owner module, which this screen does not read.
export const ACCOUNT_COLUMN = 'Hesap referansı'
export const UNMAPPED_ACCOUNT = 'Eşlenmemiş hesap'
export const ACCOUNT_REF_NOTE =
  'Hesap planı henüz tanımlı değil: satırlar hesap adı değil, eşlenmemiş bir hesap referansı taşır.'
export const SOURCE_RECORD = 'Kaynak kayıt'
export const OPEN_LINKED_JOURNAL = 'Bağlı fişi aç'
export const REF_PREFIX = 'ref.'

/** The last eight characters of a technical id, labelled as a reference; the full id stays in the title only. */
export const shortRef = (id: string) => `${REF_PREFIX} …${id.slice(-8)}`

export const QUEUES_TITLE = 'Muhasebe Kontrol'
export const EXCEPTIONS_TITLE = 'Eşleme bekleyenler'
export const EXCEPTIONS_NOTE = 'Eksik eşleme hiçbir hesaba tahminle kaydedilmez.'
export const PENDING_TITLE = 'Tutar bekleyenler'
export const PENDING_NOTE = 'Bilinmeyen tutar sıfır sayılmaz.'
export const EMPTY_EXCEPTIONS = 'Eşleme bekleyen kayıt yok.'
export const EMPTY_PENDING = 'Tutar bekleyen kayıt yok.'

export const UNKNOWN_TITLE = 'Sonuç bilinmiyor'
export const UNKNOWN_ACTION_HELP =
  'İşlem kendiliğinden tekrarlanmadı. Aynı işlemi aynı anahtarla yeniden gönderebilirsiniz; sunucu aynı isteğe ilk cevabını verir.'
export const RESEND = 'Aynı işlemi yeniden gönder'
export const FAILED_TITLE = 'Kaydedilmedi'

const CODE_TEXT: Readonly<Record<string, string>> = {
  accounting_period_overlap: 'Bu tarihler şirketin kayıtlı bir dönemiyle çakışıyor.',
  accounting_period_conflict: 'Aynı dönem kimliği başka tarihlerle kayıtlı.',
  accounting_period_preparer_conflict: 'Aynı dönem kimliğini başka bir kişi önerdi.',
  accounting_period_not_proposed: 'Dönem artık öneri değil; liste yeniden okundu.',
  accounting_period_not_open: 'Yalnız açık bir dönem kapatılır.',
  accounting_period_already_closed: 'Dönem zaten kapalı.',
  accounting_period_invalid: 'Dönem tarihleri geçerli değil.',
}

export function failureText(failure: PeriodFailure): string {
  switch (failure.kind) {
    case 'validation':
      return 'İstek geçerli değil.'
    case 'unauthenticated':
      return 'Oturumunuz sona erdi.'
    case 'forbidden':
      return 'Bu işlem için yetkiniz yok. Dönemi öneren kişi kendi önerisini onaylayamaz.'
    case 'not-found':
      return 'Dönem bulunamadı.'
    case 'version-conflict':
      return 'Dönem siz okurken değişti. Liste yeniden okundu; güncel hâline göre yeniden deneyin.'
    case 'idempotency-conflict':
      return 'Bu işlem anahtarı başka bir istekle kullanılmış. Hiçbir şey varsayılmadı.'
    case 'concurrent-change':
      return 'Aynı anda başka bir yazma oldu; hiçbir şey kaydedilmedi. Liste yeniden okundu.'
    case 'period-conflict':
    case 'rule':
      return CODE_TEXT[failure.code] ?? `Kural reddetti (${failure.code}).`
    case 'unsupported-media':
      return 'İstek biçimi desteklenmiyor.'
  }
}

export function unknownText(unknown: PeriodUnknown): string {
  switch (unknown.kind) {
    case 'offline':
      return 'Bağlantı yok; işlemin sonucu bilinmiyor.'
    case 'network-error':
      return 'Sunucudan cevap gelmedi; işlemin sonucu bilinmiyor.'
    case 'service-unavailable':
      return 'Hizmet şu an cevap veremedi; işlemin sonucu bilinmiyor.'
    case 'unexpected-success':
    case 'undecodable':
      return 'Sunucunun cevabı okunamadı; işlemin sonucu bilinmiyor.'
    case 'aborted':
      return 'Önceki işlemin sonucu bilinmiyor.'
  }
}

// ---------------------------------------------------------------------------------
// ACC-V1 journal reversal (Business #66/5827281417): propose, then ANOTHER person approves.
// ---------------------------------------------------------------------------------

export const REVERSAL_TITLE = 'Ters kayıt öner'
export const REVERSAL_INTRO =
  'Öneri kaydedilir; asıl fiş değişmez ve ters fiş henüz oluşmaz. Ters fişi, öneriyi yapandan farklı ve onay yetkisi olan bir kişi onaylayınca sistem oluşturur.'
export const REVERSAL_PERIOD = 'Ters kaydın dönemi (kimlik)'
export const REVERSAL_TRANSACTION_DATE = 'İşlem tarihi'
export const REVERSAL_ACCOUNTING_DATE = 'Muhasebe tarihi'
export const REVERSAL_VALUE_DATE = 'Valör tarihi (isteğe bağlı)'
export const REVERSAL_REASON = 'Gerekçe'
export const REVERSAL_SUBMIT = 'Ters kayıt öner'
export const REVERSAL_PROPOSED = 'Ters kayıt önerisi kaydedildi ve onay bekliyor. Asıl fiş değişmedi.'
export const REVERSAL_REPLAYED = 'Bu öneri daha önce kaydedilmişti; sunucunun kayıtlı cevabı gösterildi.'
export const REVERSAL_OPEN_APPROVAL = 'Onay ekranını aç'

export const REVERSAL_APPROVAL_TITLE = 'Ters kayıt onayı'
export const REVERSAL_OWN_PROPOSAL = 'Bu öneriyi siz hazırladınız; başka bir yetkili onaylamalı. Onay düğmesi size gösterilmez.'
export const REVERSAL_APPROVE = 'Ters kaydı onayla'
export const REVERSAL_APPROVED = 'Onaylandı: ters fiş oluşturuldu. Asıl fiş ve ters fiş birbirine bağlı kalır.'
export const REVERSAL_OPEN_SOURCE = 'Asıl fişi aç'
export const REVERSAL_OPEN_REVERSAL = 'Ters fişi aç'
export const REVERSAL_REREAD = 'Öneriyi yeniden oku'

export const REVERSAL_STATUS_LABEL: Readonly<Record<'Pending' | 'Approved' | 'Rejected', string>> = {
  Pending: 'Onay bekliyor',
  Approved: 'Onaylandı — ters fiş oluşturuldu',
  Rejected: 'Reddedildi',
}

export const REVERSAL_FORM_ISSUE: Readonly<Record<'periodId' | 'transactionDate' | 'accountingDate' | 'valueDate' | 'reason', string>> = {
  periodId: 'Geçerli bir dönem kimliği girin.',
  transactionDate: 'İşlem tarihini girin.',
  accountingDate: 'Muhasebe tarihini girin.',
  valueDate: 'Valör tarihi geçerli değil.',
  reason: 'Gerekçe boş olamaz ve 1024 karakteri geçemez.',
}

const REVERSAL_CODE_TEXT: Readonly<Record<string, string>> = {
  accounting_journal_reversal_source_journal_not_found: 'Bu şirkette terslenecek fiş bulunamadı.',
  accounting_journal_reversal_period_not_found: 'Bu şirkette seçilen dönem bulunamadı.',
  accounting_invalid_reference: 'Fiş veya dönem bağlantısı geçerli değil.',
  accounting_reversal_of_reversal: 'Bir ters kayıt yeniden terslenemez.',
  accounting_period_closed: 'Seçilen dönem kapalı; ters kayıt açık bir döneme yapılmalı.',
  accounting_period_does_not_cover_date: 'Muhasebe tarihi seçilen dönemin içinde değil.',
  accounting_period_not_open: 'Seçilen dönem henüz onaylanıp açılmadı; öneri beklemede kaldı.',
  accounting_period_not_found: 'Önerideki dönem artık bulunamıyor.',
}

export function reversalFailureText(failure: JournalReversalFailure): string {
  switch (failure.kind) {
    case 'validation':
      return 'İstek geçerli değil.'
    case 'unauthenticated':
      return 'Oturumunuz sona erdi.'
    case 'forbidden':
      return 'Bu işlem için yetkiniz yok. Öneriyi yapan kişi kendi önerisini onaylayamaz.'
    case 'not-found':
      return 'Ters kayıt önerisi bulunamadı.'
    case 'version-conflict':
      return 'Öneri siz okurken değişti. Güncel hâlini yeniden okuyun.'
    case 'idempotency-conflict':
      return 'Bu işlem anahtarı başka bir istekle kullanılmış. Hiçbir şey varsayılmadı.'
    case 'concurrent-change':
      return 'Aynı anda başka bir yazma oldu; hiçbir şey kaydedilmedi.'
    case 'content-conflict':
      return 'Aynı öneri kimliği başka içerikle kayıtlı; hiçbir şey değişmedi.'
    case 'not-pending':
      return 'Öneri artık onay beklemiyor.'
    case 'posting-conflict':
      return 'Ters fiş oluşturulamadı; hiçbir şey kaydedilmedi.'
    case 'rule':
      return REVERSAL_CODE_TEXT[failure.code] ?? `Kural reddetti (${failure.code}).`
    case 'unsupported-media':
      return 'İstek biçimi desteklenmiyor.'
  }
}

// ---------------------------------------------------------------------------------
// ACC-V1 normal journal drafts (BLG-05: every user text of NormalJournalDraftPages lives here)
// ---------------------------------------------------------------------------------

export const NORMAL_JOURNAL_TEXT = {
  contextSubject: 'muhasebe şirket bağlamı',
  draftTitle: 'Normal fiş taslağı',
  draftSubject: 'normal fiş taslağı',
  createTitle: 'Yeni normal fiş taslağı',
  createdTitle: 'Normal fiş taslağı kaydedildi',
  createHelp:
    'Hesaplar yalnız sunucudaki onaylı Mikro aynası ve posting rule ile doğrulanır. Sağlayıcı hazır değilse kayıt yapılmaz.',
  invalidSourceAndDates: 'Kaynak ve tarihler geçerli olmalıdır.',
  preparationRequired: 'Kaynağın onaylı hesap hazırlığı yüklenmelidir.',
  invalidLines: 'Her satırda geçerli hesap, taraf ve pozitif tutar gerekir.',
  reopen: 'Kaydı kapat ve yeniden aç',
  openApproval: 'Onay ekranını aç',
  createUnknown: 'Sonuç bilinmiyor. Aynı anahtar ve aynı içerikle yeniden gönderin.',
  approvalUnknown: 'Onay sonucu bilinmiyor.',
  resend: 'Aynı isteği yeniden gönder',
  source: 'Onaylı finans kaynağı',
  chooseSource: 'Kaynak seçin',
  transactionDate: 'İşlem tarihi',
  accountingDate: 'Muhasebe tarihi',
  valueDate: 'Valör tarihi',
  sourcesSubject: 'yetkili finans kaynakları',
  preparationSubject: 'onaylı hesap hazırlığı',
  preparationUnreadable: 'Kaynağın onaylı hesap hazırlığı okunamadı.',
  account: 'Onaylı hesap',
  chooseAccount: 'Hesap seçin',
  debit: 'Borç',
  credit: 'Alacak',
  amount: 'Tutar',
  removeLine: 'Satırı kaldır',
  addLine: 'Satır ekle',
  saveDraft: 'Taslağı kaydet',
  closeForm: 'Formu kapat',
  preparationChanged: 'Onaylı hesap hazırlığı değişti. Seçimleri yenileyin.',
  refreshPreparation: 'Hazırlığı yenile',
  status: 'Durum',
  version: 'Sürüm',
  sourceRef: 'Kaynak',
  postingRule: 'Posting rule',
  lineNumber: 'Satır',
  lineAccount: 'Hesap',
  lineSide: 'Taraf',
  approve: 'Normal taslağı onayla',
  openPostedJournal: 'Oluşturulan fişi aç',
  backToJournals: 'Fişlere dön',
  // BLG-05: the approval period is chosen from the readable open periods, never typed as an id.
  period: 'Onay dönemi',
  choosePeriod: 'Açık dönem seçin',
  periodsSubject: 'açık muhasebe dönemleri',
  periodListUnavailable:
    'Onay dönemi seçmek için muhasebe dönemlerini görme yetkisi gerekir. Bu yetki olmadan onay verilemez.',
  noOpenPeriod: 'Bu şirkette açık muhasebe dönemi yok. Önce bir dönemin açılması gerekir.',
  morePeriods: 'Daha eski dönemleri yükle',
  periodRequired: 'Bir açık dönem seçin.',
} as const

export function normalDraftSavedText(version: number): string {
  return `Taslak sürüm ${version} olarak kaydedildi.`
}

export function normalPreparationVersionsText(accountPlanVersion: string, ruleVersion: string): string {
  return `Onaylı ayna ${accountPlanVersion}; kural ${ruleVersion}.`
}

export function normalSourceRefText(sourceId: string): string {
  return `Kaynak ref. ...${sourceId.slice(-8)}`
}

/** An open period as the approver reads it: its date range, and whether it holds the draft's accounting date. */
export function approvalPeriodLabel(startsOn: string, endsOn: string, holdsAccountingDate: boolean): string {
  return holdsAccountingDate
    ? `${startsOn} – ${endsOn} (taslağın muhasebe tarihi bu dönemde)`
    : `${startsOn} – ${endsOn}`
}

/** The server's stable rule codes for a period that can no longer take the draft (422). */
const NORMAL_JOURNAL_CODE_TEXT: Readonly<Record<string, string>> = {
  accounting_period_closed: 'Seçilen dönem bu sırada kapandı. Açık dönemler yeniden okundu; başka bir açık dönem seçin.',
  accounting_period_not_open: 'Seçilen dönem açık değil. Açık dönemler yeniden okundu; başka bir açık dönem seçin.',
  accounting_period_does_not_cover_date: 'Seçilen dönem taslağın muhasebe tarihini kapsamıyor. Tarihi kapsayan açık dönemi seçin.',
  accounting_period_not_found: 'Seçilen dönem bulunamadı. Açık dönemler yeniden okundu.',
}

/** Codes after which the open-period list is re-read and the choice cleared. */
export const NORMAL_JOURNAL_PERIOD_CODES: ReadonlySet<string> = new Set(Object.keys(NORMAL_JOURNAL_CODE_TEXT))

export function normalJournalCreateFailureText(failure: NormalJournalFailure): string {
  return `İstek reddedildi: ${normalJournalFailureDetail(failure)}`
}

export function normalJournalApprovalFailureText(failure: NormalJournalFailure): string {
  return `Onay reddedildi: ${normalJournalFailureDetail(failure)}`
}

function normalJournalFailureDetail(failure: NormalJournalFailure): string {
  switch (failure.kind) {
    case 'validation':
      return failure.field === 'periodId' ? NORMAL_JOURNAL_TEXT.periodRequired : 'İstek geçerli değil.'
    case 'unauthenticated':
      return 'Oturumunuz sona erdi.'
    case 'forbidden':
      return 'Bu işlem için yetkiniz yok.'
    case 'not-found':
      return 'Taslak bulunamadı.'
    case 'version-conflict':
      return 'Taslak siz okurken değişti. Güncel hâlini yeniden okuyun.'
    case 'idempotency-conflict':
      return 'Bu işlem anahtarı başka bir istekle kullanılmış. Hiçbir şey varsayılmadı.'
    case 'concurrent-change':
      return 'Aynı anda başka bir yazma oldu; hiçbir şey kaydedilmedi.'
    case 'content-conflict':
      return 'Aynı taslak kimliği başka içerikle kayıtlı; hiçbir şey değişmedi.'
    case 'not-pending':
      return 'Taslak artık onay beklemiyor.'
    case 'posting-conflict':
      return 'Fiş oluşturulamadı; hiçbir şey kaydedilmedi.'
    case 'preparation-version-conflict':
      return 'Onaylı hesap hazırlığı değişti.'
    case 'rule':
      return NORMAL_JOURNAL_CODE_TEXT[failure.code] ?? `Kural reddetti (${failure.code}).`
    case 'unsupported-media':
      return 'İstek biçimi desteklenmiyor.'
  }
}
